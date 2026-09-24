# Protocole Linkpearl

Ce document décrit le protocole sur le fil, tel que le code l'implémente. Il
existe pour qu'une personne extérieure puisse le relire sans lire le code.
Chaque section renvoie au fichier qui fait foi.

> **La construction n'a pas encore été validée.** Elle est maison, et les
> vecteurs figés du dépôt ne valident que sa cohérence avec elle-même,
> puisqu'ils ont été produits par cette même implémentation. Une première
> relecture, le 24 septembre 2026, a surtout montré que la version précédente de
> ce document décrivait un modèle de confiance qui n'existait plus : voir
> [Limites connues](#limites-connues). Une relecture extérieure reste une
> condition de diffusion.

## Conventions

- Tous les entiers **du fil** sont en **big-endian** : horodatages, compteurs,
  ports, numéros de monde, longueurs. Seule exception, hors fil : le nombre
  d'itérations du fichier de sauvegarde, en little-endian (voir
  [Secrets locaux](#secrets-locaux)).
- Tout aléa et toute clé viennent de `RandomNumberGenerator` et de
  `ECDiffieHellman.Create` / `ECDsa.Create`, c'est-à-dire du générateur du
  système (CNG sous Windows). Un échec de génération lève une exception, qui
  fait échouer l'opération : il n'y a pas de repli sur une source plus faible.
- Les horodatages sont des secondes Unix sur 64 bits signés.
- Un point P-256 voyage **non compressé** (`0x04 || X(32) || Y(32)`, 65 octets),
  sauf dans les demandes de pairage où il est **compressé** (`0x02|0x03 || X(32)`,
  33 octets).
- `||` est la concaténation. `[a..b]` désigne les octets `a` inclus à `b` exclu.

## Primitives

| Rôle | Primitive |
|---|---|
| Identité, signature | ECDSA P-256, SHA-256, signature concaténée r&#124;&#124;s de 64 octets |
| Accord de clé | ECDH P-256, éphémère à chaque session |
| Dérivation | HKDF-SHA256, HMAC-SHA256 |
| Chiffrement authentifié | AES-256-GCM, nonce de 12 octets, étiquette de 16 octets |
| Empreintes, adressage de contenu | SHA-256 |

Toutes viennent de `System.Security.Cryptography`, sans dépendance. Voir
`Core/Crypto/CryptoPrimitives.cs` pour le choix de P-256 plutôt que des courbes
25519, et d'AES-GCM plutôt que ChaCha20-Poly1305.

**Validation des points.** Tout point public reçu du réseau, compressé ou non,
passe par `CryptoPrimitives.Decode` ou `Decompress` avant usage :

- longueur et préfixe exacts, sinon refus ;
- `x < p` et `y < p` ;
- `y² ≡ x³ - 3x + b (mod p)`, ou pour un point compressé, existence d'une racine
  vérifiée par élévation au carré.

Le point à l'infini n'a pas de représentation dans ces deux encodages, il ne
peut donc pas être reçu. Le cofacteur de P-256 vaut 1 : un point de la courbe
appartient au sous-groupe d'ordre premier, il n'y a pas de sous-groupe de petit
ordre à écarter. **Le runtime ne fait pas cette vérification à l'import**, d'où
le contrôle explicite.

## Identités

Chaque personnage a sa propre clé ECDSA P-256, générée localement ; seule la
clé publique sort. `PeerId = SHA-256(point non compressé)`. L'autorisation vient
**du carnet local et de lui seul** : une session n'est acceptée que si la clé
présentée a pour empreinte le `PeerId` attendu pour ce lien précis
(`PeerSession.Authorizes`), et, si le carnet la connaît déjà, si elle est
identique octet pour octet.

**La clé privée d'identité a un second usage, hors protocole** : son scalaire
`D` est l'entrée d'une dérivation `HKDF-SHA256(ikm = D, info =
"linkpearl:moodles-guid:v1")`, qui donne la clé HMAC masquant les GUID Moodles
transmis dans les manifestes (`Core/Manifest/MoodlesSanitizer.cs`). HMAC étant
une fonction pseudo-aléatoire, la sortie ne révèle rien de `D` ; mais la clé
n'est donc pas réservée à ECDSA, et une séparation propre la remplacerait par
un secret dédié.

## Modèle de confiance

C'est la section qui compte le plus, et la seule où le protocole ne tient pas
ce que la conception d'origine promettait.

**La clé publique d'un pair est apprise par le rendez-vous, au pairage.** Il
n'y a plus de code d'invitation échangé hors du jeu (voir `pairage.md`) : la
demande de pairage et sa réponse transitent par la boîte aux lettres du
service, en clair. Il en découle :

| Adversaire | Peut | Ne peut pas |
|---|---|---|
| Observateur du réseau | voir les adresses IP, les tailles, les horaires | lire une session, usurper une identité |
| Rendez-vous honnête mais curieux, ayant vu passer le pairage | savoir quels personnages se sont pairés | calculer le secret de paire, qui vient d'un accord éphémère ; lire une session |
| Rendez-vous malveillant, **au moment du pairage** | substituer sa propre clé des deux côtés et s'intercaler dans toutes les sessions suivantes de cette paire | agir sur une paire formée ailleurs |
| Rendez-vous malveillant, **après le pairage** | refuser le service, mentir sur une adresse, relayer ou non | faire accepter une autre identité : la clé est épinglée dans le carnet |

C'est donc une **confiance au premier contact** (TOFU), dont le premier contact
passe par le serveur. Pour un membre de groupe, ce premier contact n'est pas le
pairage mais le premier handshake avec lui, et il reste gagnable par qui arrive
avant le vrai : voir [Groupes (noyau)](#groupes-noyau). Pour un cercle qui héberge son propre service,
l'opérateur est l'un d'eux, et c'est assumé dans `pairage.md`. Pour le service
public, c'est la limite principale du protocole.

## Pairage

Fichiers : `Core/Sync/PairRequestMessage.cs`, `Integration/PresenceService.cs`,
`Core/Identity/PairSecret.cs`, `Core/Identity/MailboxAddress.cs`.

Chaque personnage ouvre une boîte aux lettres dont l'adresse se calcule depuis
son nom et son monde, sur une fenêtre de trente minutes :

```
empreinte = empreinte de nom@monde                             voir PlayerFingerprint
adresse   = SHA-256("linkpearl:mbox:v1" || empreinte || fenetre(8))[tronquée]
```

Le demandeur dépose dans la boîte de la cible, **sur tous les services de sa
propre liste** puisqu'il ignore lequel la cible emploie :

```
demande  = 0x03 || cleA_compressee(33) || alea_pairage(12) || ephA_compresse(33) || monde(2) || nom_utf8(≤64)
reponse  = 0x04 || cleB_compressee(33) || alea_pairage(12) || ephB_compresse(33) || monde(2) || nom_utf8(≤64)
```

Le tout en clair. La réponse reprend l'aléa de la demande, que le demandeur
retrouve dans ses demandes en attente, avec la moitié privée de `ephA`, gardée
en mémoire seulement : un plugin rechargé entre la demande et la réponse ne peut
plus conclure, et il faut redemander. `ephB` est tiré à l'acceptation. Les types
`0x01` et `0x02`, ceux de la version 1 sans éphémère, sont refusés avec un motif
lisible.

Le secret de paire :

```
materiau = ECDH(ephA, ephB) || alea_pairage
sel      = min(PeerIdA, PeerIdB) || max(PeerIdA, PeerIdB)
prk      = HKDF-Extract(sel, ikm = materiau)
secret   = HKDF-Expand(prk, "linkpearl:pair:v2", 32)
```

Il ne chiffre jamais une session. Il sert aux jetons de rendez-vous, au
scellement des candidats et au jeton de relais, c'est-à-dire à cacher au
serveur qui parle à qui. Un service qui se contente de regarder passer le
pairage ne voit que `ephA` et `ephB`, et ne peut pas calculer l'accord. Un
service qui **substitue** ses propres éphémères le peut, mais il substitue
alors aussi les identités : c'est la limite du modèle de confiance, pas une
fuite supplémentaire.

En version 1, le matériau était l'aléa seul, en clair : tout service qui voyait
passer le pairage connaissait le secret. Les paires formées avant le
24 septembre 2026 gardent ce secret-là jusqu'à ce qu'elles se pairent à nouveau.

## Groupes (noyau)

Fichiers : `Core/Groups/GroupId.cs`, `Core/Groups/GroupDerivation.cs`,
`Core/Groups/GroupBook.cs`, `Integration/GroupStore.cs`.

Un groupe est un secret de 32 octets, partagé entre ses membres. Chaque membre
ouvre une boîte de présence, dérivée du secret et de son empreinte, renouvelée
toutes les trente minutes. Le détecteur existant interroge ces boîtes pour les
joueurs visibles ; un planificateur en transforme les réponses en `PairRecord`
éphémères, que le moteur compose comme des paires ordinaires. Le handshake
autorise la session par épinglage de la première clé vue (TOFU), et choisit
l'initiateur en comparant ordinalement les empreintes en hexadécimal.

**Identifiant de groupe** :

```
GroupId = SHA-256(matériau)[0..16]
```

où le matériau est le secret du groupe (32 octets) pour un groupe partagé, ou sa clé de signature pour un groupe privé.

**Boîte de présence**, fenêtres de trente minutes :

```
fenetre      = floor(temps / 1800) en secondes Unix
adresse      = SHA-256("linkpearl:group-presence:v1" || secret (32) || empreinte (16) || fenetre(8))[0..6]
```

Exemple : secret = octets 0x00 à 0x1f, alice@21 le 2026-09-22 12:00:00 UTC
(fenêtre 994488), présence = `31e46f1f1b91`.

**Secret de paire de groupe**, déduit du secret et des empreintes des deux
membres :

```
sel      = min(empreinte A, empreinte B) || max(empreinte A, empreinte B)
prk      = HKDF-Extract(sel, ikm = secret du groupe)
secret   = HKDF-Expand(prk, "linkpearl:group-pair:v1", 32)
```

Exemple : alice et bob, secret partagé : `fd0ebba6aa5cd0fa751986531ed267f714904c16f7e47bce559b34d896041672`.

**Identifiant de runtime**, local, jamais transmis :

```
runtime_id = SHA-256("linkpearl:group-runtime:v1" || secret de paire)[0..16]
```

Exemple : `31474004ac6d8e2d031c1e3553bd693b`.

**Choix de l'initiateur** : celui des deux dont l'empreinte est ordinalement la
plus faible en hexadécimal. Les deux côtés voient le même résultat puisqu'ils
ont les mêmes empreintes.

**Modèle de confiance pour les groupes** : chaque membre connaît le secret,
donc peut calculer les jetons de tous les autres et de n'importe quel couple.
La parade : la première clé vue pour un personnage est épinglée dans le carnet
de groupe, et une autre clé pour ce personnage est « Contestée ». Le premier
contact reste gagnable par qui arrive avant le vrai membre : un membre qui se
présente sous l'empreinte d'un autre avant lui, ou quiconque a appris le
secret (un service qui s'est intercalé à l'admission, par exemple), fait
épingler sa propre clé. C'est un prix assumé, énoncé dans la spec des groupes,
et non un risque exclu. Ce qui est exclu, c'est d'épingler sans preuve : la
clé n'est présentée au carnet de groupe qu'une fois la signature du transcript
et la liaison vérifiées (voir [Liaison des identités](#liaison-des-identités)),
donc seulement par qui détient la partie privée. Une fois la clé épinglée, un
rendez-vous peut faire échouer une connexion, pas la remplacer. Le service
apprend qu'un joueur tient une boîte supplémentaire, jamais laquelle ni quel
groupe elle porte.

## Rendez-vous et connexion

Fichiers : `Core/Transport/Rendezvous/RendezvousTicket.cs`, `Core/Sync/PeerConnector.cs`,
`Core/Sync/CandidateSet.cs`, `Core/Transport/Rendezvous/RendezvousWire.cs`.

**Jetons d'annonce**, fenêtres de dix minutes, la courante et la suivante :

```
jeton(i) = HMAC-SHA256(secret, "linkpearl:rv:v1" || i(8))[0..16]
```

Le service apparie deux annonces qui partagent un jeton et renvoie à chacune le
bloc de candidats de l'autre, qu'il ne peut pas lire sans le secret.

**Bloc de candidats, format 2** (le format 1 est abandonné, voir plus bas) :

```
cle   = HKDF-Expand(secret, "linkpearl:candidates:v2", 32)
clair = n(1) || n × ( longueur(1) || adresse(4 ou 16) || port(2) )     n ≤ 8
bloc  = alea(12) || AES-GCM(cle, alea, clair, donnée associée = "linkpearl:candidates:v2")
```

Un aléa neuf à chaque annonce. Le format 1 scellait sous un nonce nul, avec une
clé fixe pour toute la vie de la paire, et les deux pairs scellaient chacun leur
bloc : chaque annonce réemployait le même couple clé-nonce sous les yeux du
service, ce qui en AES-GCM livre le XOR des clairs et de quoi forger des
étiquettes. Changer l'étiquette de dérivation retire aussi du jeu la clé dont
le format 1 a pu laisser fuir de quoi forger.

Un pair en mode **relais seul** envoie un bloc à zéro candidat.

**Perçage.** Si les deux blocs portent au moins une adresse, chaque côté tente
une connexion UDP (LiteNetLib) vers toutes les adresses de l'autre, pendant dix
secondes. La décision ne dépend que des deux blocs, que les deux côtés voient à
l'identique : ils tentent le perçage ensemble, ou passent au relais ensemble.

**Relais.** Sinon, ou si le perçage échoue, chaque côté ouvre le relais sur le
service qui a apparié, sous un jeton que les deux calculent sans échange :

```
jeton_relais = HMAC-SHA256(secret, "linkpearl:relay:v1" || min(blocA, blocB) || max(blocA, blocB))[0..16]
```

Le service met les deux connexions TCP bout à bout et recopie les trames sans
les lire. Le jeton change à chaque tentative, puisque les blocs portent un aléa.

## Handshake, SIGMA-I

Fichiers : `Core/Crypto/Handshake*.cs`, `Core/Sync/PeerSession.cs`.

Trois trames sur le canal 0. A est l'initiateur, B le répondeur. Le rôle se
décide par comparaison des `PeerId` (le plus petit initie), pas par qui a appelé :
les deux côtés se joignent en même temps.

```
prologue = "linkpearl-handshake-v1"

msg1  A -> B   clair    version(2) || horodatage(8) || ephA(65) || aleaA(16)          91 octets
msg2  B -> A   clair    version(2) || ephB(65) || aleaB(16)                           83 octets
               scellé   { idB(65) || sigB(64) || liaisonB(32) } + étiquette(16)       177 octets
msg3  A -> B   scellé   { idA(65) || sigA(64) || liaisonA(32) } + étiquette(16)       177 octets
```

`version` vaut `majeur(1) || mineur(1)`, actuellement `0x01 0x00`.

Dérivation :

```
dh      = ECDH(ephA, ephB)
th1     = SHA256(prologue || msg1 || msg2_clair)
prk     = HKDF-Extract(sel = th1, ikm = dh)
k_b     = HKDF-Expand(prk, "b->a", 32)          scelle msg2
k_a     = HKDF-Expand(prk, "a->b", 32)          scelle msg3
k_liai  = HKDF-Expand(prk, "bind", 32)
sid     = HKDF-Expand(prk, "session id", 16)
k_a2b   = HKDF-Expand(prk, "data a->b", 32)     canal de données
k_b2a   = HKDF-Expand(prk, "data b->a", 32)

sigB        = ECDSA(idB, "linkpearl-sig-responder-v1" || th1)
liaisonB    = HMAC-SHA256(k_liai, idB_pub || th1)
msg2_scellé = AES-GCM(k_b, nonce = 0, idB_pub || sigB || liaisonB, donnée associée = th1)

th2         = SHA256(th1 || msg2_scellé)
sigA        = ECDSA(idA, "linkpearl-sig-initiator-v1" || th2)
liaisonA    = HMAC-SHA256(k_liai, idA_pub || th2)
msg3        = AES-GCM(k_a, nonce = 0, idA_pub || sigA || liaisonA, donnée associée = th2)
```

Les deux trames scellées emploient un **nonce constant**, sûr ici et seulement
ici : chaque clé vient d'un accord éphémère neuf, ne chiffre qu'un seul message,
et les deux sens ont des clés distinctes.

Vérifications à la réception d'une trame scellée, dans cet ordre
(`HandshakeTranscript.TryOpenAuthentication`) : déchiffrement authentifié, taille,
validité du point, signature sur le transcript, liaison en temps constant, puis
seulement **autorisation par le carnet**. Le premier échec clôt la session.
L'autorisation vient en dernier parce que, pour un membre de groupe, elle
épingle la clé présentée : la consulter avant la preuve laisserait épingler une
clé dont le correspondant ne détient pas la partie privée.

### Liaison des identités

C'est la construction SIGMA-I de Krawczyk. Les signatures ne portent pas les
identités en clair ; ce qui lie chaque identité à la session, ce sont :

- son chiffrement sous une clé dérivée de `dh`, avec le transcript comme donnée
  associée ;
- la **liaison**, HMAC de sa propre clé publique et du transcript sous `k_liai`,
  qui est le « MAC de l'identité » de SIGMA et ce qui empêche la mauvaise
  liaison d'identité (*unknown key-share*) ;
- pour l'initiateur, sa signature porte sur `th2`, qui contient `msg2_scellé`,
  donc l'identité du répondeur ;
- côté applicatif, l'exigence que la clé reçue soit celle du pair attendu pour
  ce lien, et non n'importe quelle clé du carnet.

### Anti-rejeu

Il repose sur **les éphémères et les aléas des deux côtés**, pas sur
l'horodatage. Un `msg1` rejoué obtient un `msg2` construit sur un éphémère neuf
de B, et l'attaquant ne peut pas produire le `msg3` qui signe ce nouveau
transcript. Un `msg2` rejoué ne correspond pas au `msg1` en cours de A et
échoue au déchiffrement. Aucun état des sessions passées n'est donc nécessaire.

L'horodatage de `msg1` est vérifié par le répondeur seul, avec une tolérance de
soixante secondes d'avance ou de retard : c'est un filtre contre les vieilles
trames, pas le mécanisme anti-rejeu.

### Établissement

- **A** considère la session établie dès `msg2` vérifié et `msg3` envoyé. Il
  n'a pas de confirmation explicite que B a accepté `msg3` : la première trame
  du canal de données de B en tient lieu, et si B refuse, il ferme le lien.
- **B** la considère établie dès `msg3` vérifié.
- Chaque trame du handshake est attendue quinze secondes au plus. Tout échec,
  refus ou délai ferme le lien ; la connexion est retentée plus tard, avec un
  délai croissant.

## Canal de données

Fichier : `Core/Crypto/SecureChannel.cs`.

```
trame           = type(1) || canal(1) || compteur(8) || chiffré || étiquette(16)
nonce           = sid[0..4] || compteur(8)
donnée associée = type || canal || compteur
```

- `canal` est inférieur à 64. **L'octet de poids fort du compteur porte l'index
  de canal**, et le récepteur vérifie qu'il concorde avec l'octet `canal` de
  l'en-tête : deux canaux ne peuvent pas produire le même nonce. Les cinquante-six
  bits restants comptent les messages du canal à partir de 1.
- Les valeurs de `type` sont celles de `Core/Protocol/MessageKind.cs`. Le canal
  de données les authentifie ; la couche applicative ignore celles qu'elle ne
  connaît pas, ce qui laisse ajouter un type sans casser les anciens clients.
- La longueur du chiffré est celle de la trame moins l'en-tête et l'étiquette :
  le transport délimite les messages.
- Le récepteur tient un compteur par canal et refuse tout compteur inférieur ou
  égal au dernier accepté. **Le compteur n'avance qu'après authentification.**
- Le canal **exige un transport fiable et ordonné par canal** : une trame perdue
  ou déclassée ferait refuser la suivante. C'est ce que fournissent le mode
  `ReliableOrdered` de LiteNetLib et le relais TCP ; la perte de paquets est
  réparée sous cette couche.
- **Pas de renouvellement de clé en cours de session.** À l'épuisement d'un
  compteur (2⁵⁶ messages), l'émission lève et la session doit être renégociée.
  Une reconnexion refait un handshake complet, avec des clés neuves ; les
  sessions précédentes n'ont plus de clé en mémoire.

## Lien relayé

Fichier : `Core/Transport/RelayPeerLink.cs`.

Le relais ne porte qu'un flux ordonné. Chaque trame `RelayData` du service
(64 Kio au plus) transporte :

```
fragment = sorte(1) || canal(1) || données
sorte    : 0x00 fragment suivi d'autres, 0x01 dernier fragment,
           0x02 sonde (horodatage local(8)), 0x03 écho de sonde
```

Un message du canal de données est découpé en fragments de 32 Kio au plus, tous
émis sans entrelacement avec un autre message ; le récepteur les recolle par
canal, et ferme le lien sur tout message de plus de 16 Mio ou toute sorte
inconnue. Le contenu est une trame du canal de données, déjà scellée : le
service transporte sans pouvoir lire.

## Secrets locaux

Fichiers : `Integration/DpapiIdentityStore.cs`, `Integration/PairBookStore.cs`,
`Core/Identity/IdentityBackup.cs`.

| Secret | Où | Protection |
|---|---|---|
| Clé privée d'identité | `characters/<empreinte>/` du dossier de configuration | DPAPI, portée utilisateur Windows, entropie `linkpearl:identity:v1` |
| Carnet, secrets de paire compris | même dossier | DPAPI, même portée |
| Éphémère d'une demande de pairage en attente | mémoire seule | perdue au rechargement du plugin |
| Clés de session | mémoire seule | jamais écrites |

DPAPI protège contre un autre compte de la machine et contre la copie du
fichier, pas contre un programme qui tourne déjà sous le compte du joueur.

**Sauvegarde.** Un fichier exporté à la demande, qui survit à une
réinstallation du système. Avec mot de passe : PBKDF2-SHA256 (600 000
itérations par défaut, bornées entre 100 000 et 10 000 000 à la lecture), sel
de 16 octets, AES-256-GCM avec l'en-tête en donnée associée. **Sans mot de
passe, choix laissé à l'utilisateur, le fichier contient la clé privée en
clair** et n'est protégé que par un contrôle SHA-256 contre la corruption :
qui le récupère se fait passer pour le personnage.

**Effacement.** Le code est en C# managé : les clés dérivées et les secrets
sont des tableaux que le ramasse-miettes peut copier, et leur effacement n'est
pas garanti. Seul le secret brut de l'accord de pairage est explicitement mis à
zéro. C'est une limite, pas une garantie.

**Retrait d'un pair.** Le pair passe à l'état `Revoked` : on le joint encore,
mais la session ne porte plus que l'avis de retrait, jamais une apparence, et
on referme après trente secondes s'il ne raccroche pas. Son secret de paire
reste au carnet jusqu'à l'oubli de l'avis. Il n'existe **aucune rotation
d'identité** : une clé perdue ou compromise impose de recréer le personnage
côté plugin et de se pairer à nouveau avec chacun, sans moyen de prévenir les
pairs que l'ancienne clé ne doit plus être crue.

## Bornes et déni de service

Ce qu'un tiers ou un rendez-vous peut faire consommer, et ce qui l'arrête.

| Ressource | Borne | Où |
|---|---|---|
| Trame du service | 64 Kio | `RendezvousWire.MaxFrameLength` |
| Dépôt dans une boîte | 512 octets | `RendezvousWire.MaxDepositLength` |
| Bloc de candidats | 4 Kio, 8 adresses | `RendezvousWire`, `CandidateSet` |
| Trames par adresse et par minute (service public) | 60 | `RendezvousLimits` |
| Connexions simultanées par adresse (un /64 en IPv6) | 32 | `RendezvousLimits` |
| Attente d'un partenaire au relais | 30 s côté service, 20 s côté client | `RendezvousLimits`, `PeerConnector` |
| Trame du handshake attendue | 15 s | `PeerSession` |
| Message reconstitué sur le relais | 16 Mio | `RelayPeerLink` |
| Nouvelle tentative après échec | 5 s, doublée jusqu'à 5 min | `SyncEngineSettings` |

Un rendez-vous malveillant peut toujours refuser tout service : c'est la
raison d'être de la liste de services. Un pair malveillant, déjà au carnet,
peut faire échouer ses propres sessions ; les manifestes et fichiers qu'il
envoie passent par `Core/Safety` avant tout usage, et un manifeste qui viole une
seule règle est rejeté en entier.

## Métadonnées visibles

Le chiffrement protège le contenu, pas les métadonnées.

| Qui | Connexion directe | Connexion relayée |
|---|---|---|
| Le pair | votre adresse IP, publique et locales | vos adresses aussi si le relais suit un perçage raté ; aucune en mode relais seul |
| Le rendez-vous | votre adresse IP, vos horaires de présence, que vous vous annoncez, la taille du bloc de candidats | tout cela, plus la durée, le volume et le rythme de la session relayée |
| Le réseau | les deux adresses, les volumes, les horaires | votre adresse et celle du service |

S'y ajoute, au pairage, tout le contenu des demandes (voir
[Pairage](#pairage)), et en permanence l'existence de votre boîte, dont
l'adresse se calcule depuis votre nom : le service sait qui est en ligne et
quand.

## Versionnage

- **Handshake** : `version` porte un majeur et un mineur. Le majeur doit être
  identique, sinon refus. Le mineur est transmis mais **n'est pas lu** : aucune
  négociation n'existe encore.
- **Pairage** : types `0x03` et `0x04` depuis le 24 septembre 2026, avec
  éphémère. Les types `0x01` et `0x02` sont refusés.
- **Bloc de candidats** : format 2 depuis le 24 septembre 2026, incompatible
  avec le format 1. Deux clients de formats différents échouent à ouvrir le
  bloc l'un de l'autre et ne se connectent pas ; il faut que les deux soient à
  jour.
- Les chaînes de dérivation portent leur version (`…:v1`, `…:v2`) : un
  changement de format change l'étiquette.
- Une incompatibilité se signale par un refus, jamais par une lecture
  dégradée : majeur différent, type de demande de la version 1, bloc de
  candidats qui ne s'ouvre pas. Le motif est journalisé ; seul l'échec de
  connexion apparaît au joueur, en infobulle sur le pair. Une demande de
  pairage d'un ancien client est écartée sans rien afficher. Aucun chemin ne
  retombe sur un format plus ancien.

## Limites connues

Par ordre d'importance.

1. **Le rendez-vous qui voit un pairage peut s'y intercaler, et rien ne permet
   de le détecter.** La clé publique arrive par lui, en clair. Voir
   [Modèle de confiance](#modèle-de-confiance). Aucune vérification hors bande
   n'est proposée : c'est une limite d'authentification, pas seulement de
   confidentialité.
2. **Les paires formées avant le 24 septembre 2026** ont un secret dérivé de
   l'aléa seul, que tout service ayant vu leur pairage connaît. Ces services
   peuvent relier leurs présences dans le temps et lire leurs adresses
   candidates. Se pairer à nouveau suffit ; rien ne l'impose aujourd'hui.
3. **Le contenu des demandes est visible du service** : nom, monde et clé
   publique, déposés sur tous les services de la liste du demandeur. C'est le
   nom en clair qui permet au destinataire de reconnaître le demandeur.
4. **Aucune rotation d'identité** : une clé compromise ne peut pas être
   révoquée auprès des pairs. Voir [Secrets locaux](#secrets-locaux).
5. **Une sauvegarde sans mot de passe vaut l'identité**, en clair.
6. La clé privée d'identité sert aussi, par HKDF, à masquer les GUID Moodles.
7. L'effacement des secrets en mémoire n'est pas garanti (C# managé).
8. Pas de renouvellement de clé en cours de session, pas de négociation du
   mineur.
9. `Core/Crypto/ShortAuthString.cs` (six mots tirés d'une liste de 64, soit
   36 bits dérivés de `sid`) existe mais **n'est affiché nulle part**. Il
   n'apporte donc aucune protection aujourd'hui, et rien dans ce document ne
   doit être lu comme s'il en apportait une.

## Vecteurs figés

`Linkpearl.Core.Tests/Fixtures/protocol-vectors.json` couvre la dérivation de
clés et le format de trame du canal, qui sont déterministes. Le handshake tire
des éphémères et des aléas, ses trames ne sont donc pas reproductibles.

Régénérer : `dotnet run --project Linkpearl.Harness -- vectors`. **Une
modification de ce fichier est un changement de protocole sur le fil**, et doit
s'accompagner d'une montée de version.
