# Linkpearl : synchronisation P2P de l'apparence moddée dans FFXIV

## Contexte

Mare Synchronos, le plugin qui permettait à plus de 20 000 joueurs de FFXIV de se voir
mutuellement avec leurs mods, a fermé en août 2025 après une mise en demeure de l'équipe
juridique de Square Enix. Les forks qui lui ont succédé (PSyncClient, XIV Sync, Snowcloak,
Lightless) ont tous conservé son architecture : un hub central plus un serveur de fichiers
qui stocke et redistribue les mods de tout le monde. Chacun reproduit donc le point de
coupure unique qui a tué l'original, et chacun demande à ses utilisateurs de confier leurs
données à un tiers.

Ce projet construit le même service sans jamais centraliser ni les fichiers ni les données de
personnage : les pairs se transfèrent directement leurs mods, chiffrés de bout en bout, et la
seule infrastructure est un service de rendez-vous qui ne comprend rien à ce qu'il route.

Le résultat visé : deux joueurs qui se sont explicitement appairés se voient mutuellement avec
leurs mods, sans qu'aucun serveur n'ait jamais détenu un seul de leurs fichiers.

### Ce que ce projet ne résout pas, et qu'il faut savoir avant de commencer

Le 28 août 2025, Naoki Yoshida a publié une déclaration sur le Lodestone. La ligne rouge qu'il
y nomme n'est pas l'hébergement de fichiers : c'est **le fait que les autres joueurs voient vos
mods**. Le P2P supprime la cible « opérateur de serveur de fichiers », mais pas l'objection de
fond. L'auteur du plugin, l'hébergeur du dépôt Dalamud tiers et l'opérateur du rendez-vous
restent identifiables. C'est une décision assumée, pas un angle mort.

---

## Décisions arrêtées

| # | Décision | Raison |
|---|---|---|
| 1 | Rendez-vous minimal auto-hébergeable, **avec relais chiffré de secours** | Le punching seul donne 60 à 75% de paires connectables. Sur un cercle de 8, deux personnes n'auraient jamais rien. |
| 2 | Périmètre v1 : Penumbra + Glamourer, **apparence statique du joueur seul**, pairs directs | Les ressources transitoires et les objets possédés sont la moitié du travail côté jeu. Annoncé clairement : les animations moddées ne passeront pas en v1. |
| 3 | Architecture neuve, reprise de code autorisée | Le **Mare original est sous MIT**, pas AGPL. Seuls les forks récents sont AGPL. Vérifier l'en-tête du fichier repris, pas la licence du dépôt. |
| 4 | Transport 100% C# managé, aucune dépendance native | Le plugin tourne dans le processus du jeu. Pas de P/Invoke, pas de sidecar. |
| 5 | Crypto **100% CNG intégrée** : ECDSA P-256, ECDH P-256, HKDF-SHA256, AES-256-GCM, SHA-256 | Zéro dépendance, y compris BouncyCastle. Voir la justification ci-dessous. |
| 6 | Diffusion en cercle restreint, dépôt Dalamud tiers personnel | Le dépôt officiel est hors de question de toute façon. |
| 7 | **Boucle jeu d'abord, réseau ensuite** | La partie côté jeu est la plus dense en pièges et se teste sans aucun pair. |
| 8 | `Core/` ne référence jamais Dalamud, testé sous Linux | Reprise du pattern d'`EorzeaD10`, avec un test d'architecture qui le fait respecter. |

### Pourquoi pas Ed25519 ni ChaCha20

`ChaCha20Poly1305` de .NET exige **Windows 10 build 20142 ou plus**, soit Windows 11 en
pratique. Windows 10 22H2 est le build 19045 : `IsSupported` serait faux pour environ un quart
des joueurs, et **les tests sous Linux ne le détecteraient jamais** puisque OpenSSL le
supporte. Ed25519 et X25519 n'existent toujours pas dans `System.Security.Cryptography` en
.NET 10 (dotnet/runtime#63174, milestone « Future »), et les seules implémentations managées
sont lentes : le ChaCha20 de BouncyCastle est du C# scalaire, 150 à 400 Mo/s contre 2 à 6 Go/s
pour `AesGcm` accéléré par AES-NI, ce qui se voit en chute de framerate sur un transfert de
500 Mo dans le processus du jeu.

Tout en CNG intégré donne : zéro dépendance, accélération matérielle, compatibilité Windows 10.

### Pourquoi SIGMA-I et pas Noise_IK

Il n'existe aucune implémentation Noise en C# managé pur et maintenue. Noise_IK suppose par
ailleurs que l'initiateur connaît la clé statique d'échange du répondeur, alors que notre
identité est une clé de signature. SIGMA-I sépare proprement l'accord de clé (ECDH éphémère,
donc confidentialité persistante) de l'authentification (signature sur le transcript), et tient
en 150 lignes lisibles.

Contrepartie honnête : Noise dispose de vecteurs de test officiels externes, SIGMA-I maison non.
Mitigation obligatoire : `docs/protocol.md` écrit avant le code, vecteurs figés dans le dépôt,
et **relecture de la construction par quelqu'un d'autre avant toute diffusion**.

---

## Architecture

### Découpage

```
Linkpearl/
  Linkpearl.sln
  Directory.Build.props              Nullable enable, TreatWarningsAsErrors, LangVersion
  build/Core.Deps.props              PackageReference du noyau, source unique de vérité
  build/Core.Sources.props           <Compile Include=".../Core/**/*.cs" LinkBase="Core" />
  CLAUDE.md
  docs/protocol.md  docs/threat-model.md  docs/manual-tests.md

  Linkpearl/                         plugin Dalamud, net10.0-windows7.0
    Core/                            LE NOYAU, zéro using Dalamud
      Abstractions/    IModRepository IAppearanceRepository IGameObjectSource
                       IFrameworkDispatcher IApplyGate ILogSink IClock IIdentityStore
      Identity/        IdentityKeyPair PeerIdentity PairingCode PairingCodec PairBook
      Crypto/          CryptoPrimitives HandshakeInitiator HandshakeResponder
                       SessionKeys SecureChannel NonceSequence ShortAuthString
      Protocol/        ProtocolVersion WireFormat MessageKind Messages/ MessageReader/Writer
      Transport/       IPeerLink IPeerLinkFactory LiteNetLinkFactory LiteNetPeerLink
                       SealedPacketLayer ChannelPlan RateLimiter RelayLink
        Rendezvous/    RendezvousTicket IRendezvousClient CandidateSet HolePuncher
      Manifest/        CharacterManifest FileReplacement ManifestBuilder
                       ManifestDiff ManifestCodec
      Cache/           BlobHash IBlobStore FileSystemBlobStore BlobWriter CacheEviction
                       LocalHashIndex
      Transfer/        BlobRequestPlanner BlobSender BlobReceiver TransferProgress
      Sync/            SyncEngine PeerSession PeerSessionState LocalAppearanceTracker
                       RemoteApplicator VisibilityMatcher Debouncer
      Safety/          ExtensionAllowList GamePathPolicy Quotas ManifestValidator
    Integration/                     adaptateurs Dalamud, couche mince sans logique
      PenumbraIpc.cs GlamourerIpc.cs DalamudObjectSource.cs FrameworkDispatcher.cs
      DalamudLogSink.cs DpapiIdentityStore.cs ApplyConditions.cs
    Ui/                              MainWindow PairsTab TransfersTab SettingsTab Theme

  Linkpearl.Core.Tests/              net10.0, xunit, inclusion de sources, tourne sous Linux
  Linkpearl.Rendezvous/              net10.0, serveur autonome, signalisation + relais
  Linkpearl.Harness/                 net10.0, console, harnais deux instances
  scripts/deploy-plugin-dev.sh  scripts/release-plugin.sh
```

`build/Core.Deps.props` est importé par les quatre `.csproj` : c'est ce qui évite la dérive de
versions que la duplication de `PackageReference` provoquerait. C'est la seule addition au
pattern d'`EorzeaD10`, dont le noyau n'avait aucune dépendance NuGet.

### Interfaces de frontière

Le noyau ne connaît ni `Guid` de collection Penumbra, ni index d'objet Dalamud, ni thread de
framework. Tout passe par des abstractions implémentées dans `Integration/`.

```csharp
public readonly record struct GameObjectRef(int ObjectIndex, ulong StableId);
public readonly record struct ModTargetHandle(Guid Value);
public readonly record struct PlayerFingerprint(ulong Lo, ulong Hi);   // SHA-256 tronqué

public interface IModRepository
{
    Task<IReadOnlyDictionary<string, string>> ResolveLocalReplacementsAsync(CancellationToken ct);
    Task<string> GetMetaManipulationsAsync(CancellationToken ct);
    Task<ModTargetHandle> AcquireTargetAsync(GameObjectRef obj, CancellationToken ct);
    Task ApplyAsync(ModTargetHandle target, IReadOnlyDictionary<string, string> pathMap,
                    string metaManipulations, CancellationToken ct);
    Task ReleaseAsync(ModTargetHandle target, CancellationToken ct);
    Task RedrawAsync(GameObjectRef obj, CancellationToken ct);
    event Action? LocalModsChanged;
}

public interface IAppearanceRepository
{
    Task<string?> GetStateAsync(GameObjectRef obj, CancellationToken ct);
    Task ApplyStateAsync(GameObjectRef obj, string base64State, CancellationToken ct);
    Task RevertAsync(GameObjectRef obj, CancellationToken ct);
    event Action<GameObjectRef>? LocalStateChanged;
}

public interface IApplyGate            // conditions d'application, alimenté par Dalamud
{
    bool CanApply(out string reason);  // combat, performance, cinématique, GPose, zoning, drawing
    event Action? BecameApplicable;
}
```

Détail qui compte : **c'est l'adaptateur Dalamud qui hache le nom de personnage**. Le noyau ne
manipule jamais un nom en clair, ce qui rend structurellement impossible qu'un nom fuite dans
une trame ou un journal.

`IPeerLink` abstrait le transport, ce qui permet de tester sans réseau, de simuler perte et
latence, et de brancher le relais sans que le reste du code le sache.

### Identité et pairage

Paire **ECDSA P-256** générée au premier lancement, clé privée protégée par DPAPI
(`ProtectedData.Protect`, scope `CurrentUser`). `PeerId = SHA-256(pubkey)[0..16]`.

Le code d'invitation embarque **la clé publique complète et l'URL du rendez-vous** :

```
LP1-<base32 Crockford du corps>@rdv.exemple.ch
corps = version(1) || pubkey(33, compressée) || pairing_nonce(16) || flags(1) || crc32c(4)
```

Base32 Crockford (pas de I, L, O, U ; insensible à la casse ; dictable au vocal), groupé par 6.
Le CRC32C détecte une faute de frappe **avant toute opération réseau**, ce qui donne un message
d'erreur utile plutôt qu'un échec de connexion opaque.

Avoir la clé publique dans le code supprime toute confiance au rendez-vous pour le premier
contact : il ne peut pas substituer une clé, donc pas d'homme du milieu. Le serveur est dans le
code parce que sans cela, deux pairs configurés sur deux rendez-vous différents ne se
trouveraient jamais. Règle v1 : les deux pairs utilisent le même rendez-vous.

En ceinture et bretelles, après l'échange de clés, chaque client affiche un **SAS** (six mots
tirés d'une liste fixe) dérivé du transcript. Les deux joueurs le comparent en jeu, ils se
parlent déjà puisque le pairage est mutuel. Puis la clé est épinglée.

Le consentement est mutuel et explicite : B colle le code et passe en `Pending`, A voit une
demande entrante avec l'empreinte et accepte, refuse ou bloque.

```csharp
public sealed record PairRecord(
    PeerId Id, byte[] PublicKey, byte[] PairSecret,
    string DisplayName,             // local, jamais transmis
    PairTrust Trust,                // Pending | Accepted | Blocked
    PairPermissions Permissions,    // ReceiveAppearance / SendAppearance séparés
    ConnectionPolicy Policy,        // Direct | RelayOnly
    DateTimeOffset PairedAt, DateTimeOffset LastSeenAt);
```

`pairs.json` est chiffré par DPAPI comme l'identité, puisqu'il contient les secrets de pairage.
Prévoir l'export et l'import de l'identité : sans cela, réinstaller Windows fait perdre tous
les pairages.

### Rendez-vous, et ce qu'il apprend

Pour chaque pair accepté et chaque fenêtre de 10 minutes :

```
Ticket(pair, t) = HMAC-SHA256(PairSecret, "lp:rv:v1" || floor(unix / 600))[0..16]
```

Le client annonce les tickets de la fenêtre courante et de la suivante, pour absorber la
bascule. Le serveur est une table en mémoire `ticket -> { endpoint source, blob opaque,
expiration }`, sans aucun état persistant. Deux annonces sur le même ticket déclenchent
l'échange mutuel des endpoints et blobs, puis la séquence de punch. Le blob de candidats est
chiffré sous une clé dérivée du secret de pairage.

**Ce que le serveur apprend, à écrire tel quel dans `docs/threat-model.md`** : l'adresse IP
source, des tickets opaques qui tournent toutes les 10 minutes, et le fait que deux adresses
partagent un ticket dans une fenêtre. Il n'apprend ni clé publique, ni nom de personnage, ni
manifeste, ni fichier. Un observateur ne peut pas relier deux fenêtres entre elles.

S'il est malveillant, il peut refuser le service ou mentir sur un endpoint, ce qui produit un
échec de handshake. **Jamais une usurpation**, puisque l'autorisation vient du carnet local et
que la clé publique vient du code d'invitation.

Protections : preuve de possession du ticket par un second HMAC, plafond d'annonces par adresse,
TTL court, aucune base de données.

### Établissement de connexion

Ordre de tentative, en parallèle avec budget de temps :

1. **IPv6 direct**, 2 s. S'il y a une IPv6 globale des deux côtés, il n'y a pas de NAT du tout,
   juste un pare-feu d'état que le punch ouvre trivialement. C'est la mitigation la plus
   rentable et elle est gratuite.
2. IPv4 LAN, 1 s (cas de deux joueurs sous le même toit).
3. IPv4 punch simultané, 5 s, **par rafales avec retries** : `NatPunchModule` n'envoie que deux
   paquets sans retry, ce qui est insuffisant.
4. **Relais chiffré** via le rendez-vous, en secours automatique, ou d'emblée si le pair est en
   `RelayOnly`.

Le relais est un tuyau d'octets WebSocket qui transporte exactement les mêmes trames scellées
que le lien direct. Le serveur ne voit que du chiffré. Débit plafonné par serveur.

Le mode `RelayOnly` par pair règle aussi un problème de vie privée que Mare n'avait pas : une
connexion directe révèle votre adresse IP à votre pair, donc votre ville et votre FAI. La
première fenêtre de pairage doit le dire en une phrase claire.

### Transport des blobs

LiteNetLib est un transport de jeu, pas un outil de transfert de fichiers. Trois limites
vérifiées dans son code source, qui dictent la forme du nôtre :

- `DefaultWindowSize = 64` est une **constante**, pas un réglage. La fenêtre d'un canal fiable
  est donc figée à environ 91 KiB en vol, ce qui plafonne un canal à 1,5 Mo/s à 60 ms de RTT.
- La file d'envoi est une `Queue<NetPacket>` **non bornée sans contre-pression** : un `Send` de
  500 Mo alloue 500 Mo de paquets dans le processus du jeu.
- Il n'y a **aucun contrôle de congestion**, et le mainteneur a fermé la demande de fenêtre
  adaptative.

Conception qui en découle, non négociable :

- **Multi-canal dès la première ligne.** `ChannelsCount = 24`. Canal 0 en contrôle, jamais
  bloqué par les données. Canaux 1 à 23 pour les blobs, attribution round-robin pondérée par la
  taille restante, encapsulée dans `ChannelPlan` (testable sans réseau).
- **Blocs applicatifs de 16 KiB.** Jamais un gros buffer confié à LiteNetLib. Jamais de tableau
  de plusieurs Mo sur le LOH, sinon pauses GC visibles en jeu : `ArrayPool` partout.
- **`RateLimiter` en token bucket avec AIMD** : départ à 512 Ko/s, rampe de 128 Ko/s toutes les
  2 s tant que la perte reste sous 1% et que le ping ne dépasse pas de 30% son minimum observé,
  réduction par 0,7 sinon. Plafond configurable, défaut 8 Mo/s, **pause automatique en combat et
  en duty**. Sans cela, l'uplink sature, le bufferbloat monte, et le ping FFXIV part à 300 ms
  pendant qu'un ami télécharge les textures. Mare avait un limiteur pour cette raison exacte.
- **`SealedPacketLayer`** : les en-têtes, ACK et `Disconnect` de LiteNetLib restent en clair et
  forgeables si on ne chiffre qu'au niveau applicatif. Quiconque connaît l'endpoint peut couper
  la session. Un `PacketLayerBase` qui scelle chaque datagramme en AEAD ferme cette porte. La
  `ConnectionRequest` n'est acceptée que depuis l'endpoint attendu avec le jeton attendu.
- `PollEvents()` appelé depuis `Framework.Update`, jamais `UnsyncedEvents = true`. Hachage et
  entrées-sorties disque sur le pool de threads.

**Porte de sortie, à s'autoriser dès maintenant** : si la mesure du jalon 2 ne passe pas avec 24
canaux, LiteNetLib fait 5000 lignes en MIT. Le vendoriser en portant `DefaultWindowSize` à 256
est un changement d'une ligne contre l'engagement de maintenir un fork. L'alternative est un ARQ
applicatif sur `Unreliable`, environ trois semaines et de la dette.

### Manifeste et cache

```csharp
public sealed record FileReplacement(
    IReadOnlyList<string> GamePaths,   // plusieurs chemins pour un même contenu
    BlobHash Hash, long Size);         // SHA-256, jamais SHA-1
```

Le **regroupement par hash plutôt que par chemin** donne la déduplication gratuitement : un
`.tex` référencé par six chemins de jeu, c'est une entrée, un blob, six clés dans le `pathMap`
passé à Penumbra. Deux amies qui utilisent la même texture de peau ne la téléchargent qu'une
fois. SHA-256 et non SHA-1 comme Mare : dans un cache adressé par contenu alimenté par un tiers,
les collisions à préfixe choisi de SHA-1 sont un vecteur réel.

Sérialisation `System.Text.Json` avec `JsonSerializerContext` source-generated (pas de
réflexion, cela compte pour le temps de chargement du plugin), encodage canonique (ordre fixe,
pas d'espaces, NFC, chemins en minuscules), puis Brotli niveau 5. Tailles attendues : 4 à 8 Ko
compressés pour un personnage modéré, 20 à 50 Ko pour un personnage lourd.

Cache : **`%LOCALAPPDATA%\Linkpearl\cache` par défaut, répertoire et quota réglables par
l'utilisateur**, comme le faisait Mare. Surtout pas le répertoire de configuration du plugin,
qui est parfois sauvegardé ou synchronisé.

C'est une fonction attendue, pas un confort : le cache pèse des dizaines de Go et beaucoup de
gens le mettront sur un disque secondaire. Les réglages exposent donc :

- **le répertoire**, avec sélecteur, vérification que le chemin est accessible en écriture et
  affichage de l'espace libre du volume ;
- **le quota en Go**, avec l'occupation courante et le nombre de blobs ;
- **un bouton de purge** (tout, ou les blobs d'un pair donné, ce dernier servant aussi de
  « bloquer et purger ») ;
- l'état du hachage initial du dossier Penumbra, qui est la seule opération longue au premier
  lancement.

Pièges à traiter, qui font que ce n'est pas qu'un champ de configuration :

- `incoming/` doit rester sur **le même volume** que `blobs/`, sinon `File.Move` cesse d'être
  atomique. Le répertoire choisi porte donc les deux, jamais l'un sans l'autre.
- Changer de répertoire déplace les blobs existants plutôt que de les perdre, en tâche de fond
  avec progression, et refuse de démarrer le déplacement si le volume cible n'a pas la place.
  Un déplacement interrompu doit être repris, pas recommencé.
- Baisser le quota sous l'occupation courante déclenche une éviction immédiate, en épargnant les
  blobs épinglés par un manifeste actuellement appliqué.
- Si le volume tombe sous un seuil d'espace libre, le cache se met en lecture seule et
  l'interface le dit, plutôt que de remplir le disque de l'utilisateur.
- Des centaines de fichiers binaires écrits par `ffxiv_dx11.exe` déclenchent parfois des
  heuristiques antivirus. Nom de répertoire explicite et documenté, pour que l'exclusion soit
  facile à écrire.

```
cache/
  blobs/ab/cd/<64 hex>        deux niveaux de 256 : 1,5 fichier par feuille à 100 000 blobs
  incoming/<guid>.part        MÊME volume que blobs/, pour que File.Move soit atomique
  cache.index                 { hash, taille, dernierUsage }, réécrit toutes les 60 s
  localhash.index             chemin + taille + mtime -> hash
```

`localhash.index` n'est pas une optimisation : un dossier Penumbra fait couramment 20 à 60 Go, et
sans index chaque changement de tenue rehacherait des gigaoctets. Premier lancement : hachage
incrémental de fond, bridé, avec barre de progression.

Intégrité : hash calculé en flux pendant la réception via `IncrementalHash`, `.part` supprimé si
non conforme, `File.Move(overwrite: false)` atomique sur le même volume, revérification
paresseuse en arrière-plan et systématique après détection d'arrêt brutal par fichier sentinelle.
Revérifier 20 Go à chaque démarrage serait inacceptable.

Éviction LRU, quota défaut 20 Go, seuil bas à 85%, en excluant les blobs épinglés par un
manifeste appliqué. Sur un `Task` dédié, jamais sur le thread du framework. Ne pas se fier à
l'atime NTFS, désactivé par défaut depuis Vista : tenir `LastUsedTicks` en mémoire.

Ce que le cache apporte vraiment : la déduplication est globale, puisque le nom du fichier est
le hash de son contenu. Si trois pairs utilisent la même texture de peau, elle n'est téléchargée
qu'une fois, quel que soit celui qu'on croise en premier. Et un pair revu le lendemain sans
changement coûte **zéro octet** : son manifeste arrive, le diff est vide, l'application est
immédiate.

**Le cache ne sert jamais à afficher un pair injoignable.** Pas de session établie, pas
d'application : le pair garde son apparence par défaut et l'interface en donne la raison. Une
apparence périmée ne peut pas être distinguée d'une apparence courante par celui qui la regarde,
et rien ne garantit que le pair consentirait encore à la montrer. Le cache accélère, il ne
remplace pas le consentement.

### Application, et les conditions qui la gouvernent

C'est ici que Mare crashait, et c'est donc ici que la rigueur paie.

`IApplyGate` diffère l'application en combat et en performance musicale, bloque en cinématique et
en GPose, annule tout sur `BetweenAreas`, et attend que plus rien ne soit en train d'être dessiné
avant un redraw. Un redraw pendant un chargement de zone est un crash classique.

Ordre strict, tout sur le thread du framework :

1. `pathMap` construit depuis le manifeste vers les chemins du cache.
2. `CreateTemporaryCollection` puis `AssignTemporaryCollection(force: true)`.
3. `AddTemporaryMod` avec le même tag, ce qui rend l'appel atomique et évite l'état
   intermédiaire.
4. `RedrawObject`.
5. **Après** le redessin, `ApplyState` Glamourer avec une **clé de verrou non nulle et constante
   par plugin**. Sans clé, l'automation Glamourer du receveur écrase l'état appliqué.

L'ordre Penumbra, puis redessin, puis Glamourer n'est pas une préférence : c'est ce que les
clients dérivés de Mare ont dû corriger explicitement.

Concurrence : `SemaphoreSlim(1)` global sur l'application, file par objet, la dernière demande
gagne. Numéro de version monotone sur les manifestes pour ignorer un manifeste périmé lors d'un
changement de tenue rapide. Debounce de 750 ms avec plafond à 5 s.

**Nettoyage**, aussi important que l'application : l'`AssemblyLoadContext` de Dalamud est
collectible et le déchargement est coopératif. Un thread LiteNetLib encore vivant empêche le
déchargement, le plugin fuit, et le rechargement suivant en crée un second. `Dispose` doit donc
arrêter le `NetManager`, fermer le WebSocket, annuler toutes les tâches, désabonner tous les
événements IPC, puis `RevertState` et `UnlockState` sur chaque acteur avec notre clé, supprimer
chaque collection temporaire, et redessiner. Prévoir aussi un nettoyage **au chargement** de
toute collection portant notre préfixe, pour rattraper un crash précédent.

### Sécurité

Le vrai risque n'est pas théorique : un `.mdl` ou un `.tex` malformé est parsé par du code natif
du client FFXIV, écrit sans l'hypothèse qu'un attaquant contrôle l'entrée. **Aucune liste
blanche ne referme cette surface.** C'est pourquoi le pairage doit rester une décision
individuelle et explicite, présentée pour ce qu'elle est : une phrase claire dans la fenêtre de
premier pairage, pas une page de conditions.

Ce qui réduit quand même la surface :

- **Liste blanche v1**, appliquée à l'extension du chemin de jeu : `.tex .mdl .mtrl .sklb .skp
  .phyb .pbd .eid .imc`. Un manifeste contenant autre chose est **rejeté en entier**, pas
  seulement l'entrée fautive : un rejet partiel donne un personnage incohérent et masque une
  tentative. `.shpk` (bytecode de shader), `.scd` (audio) et les extensions transitoires ne sont
  pas dans le périmètre v1 et restent exclus.
- **`GamePathPolicy`** : caractères en `[a-z0-9_/.-]`, pas de `..`, pas de `\`, pas de chemin
  absolu, longueur max 256, profondeur max 16, préfixe dans un ensemble connu. Entièrement
  testable, et c'est le test le plus rentable du projet.
- **Plafonds** configurables : 2000 remplacements, 8000 chemins, 128 Mo par blob, 1 Go par
  manifeste, 512 Ko de manipulations méta, 64 Ko de Glamourer, 16 Mo décompressés. Tout
  dépassement coupe la session avec une raison journalisée.
- **Décompression bornée** : Brotli lu à travers un flux compteur qui s'arrête au plafond. Jamais
  de `ReadToEnd` sur une entrée contrôlée par le pair.
- **Rien de ce qui vient du réseau ne devient un nom de fichier.** Le nom du blob est le hash
  **recalculé localement**.
- Permissions par pair, bouton « bloquer et purger » qui supprime le cache du pair, pause globale
  et pause par pair.
- L'empreinte `nom@monde` d'un pair est épinglée au pairage : un pair qui annoncerait l'empreinte
  d'un tiers pourrait sinon faire appliquer ses fichiers sur ce tiers, visible chez vous seul.
- Journalisation : jamais de nom de personnage ni de chemin local complet par défaut. Ne jamais
  utiliser l'Account ID d'autrui, ce que les règles Dalamud interdisent explicitement.

---

## Jalons

L'ordre lève les risques du plus dense au moins dense, et les deux premiers ne nécessitent
aucun code réseau.

### Jalon 0 : décider et mesurer (1 journée, zéro code)

1. **Décision d'exposition** prise consciemment : qui héberge le dépôt, qui opère le rendez-vous,
   sous quel nom, au vu de la déclaration de Yoshida.
2. **Volume réel** : mesurer 3 ou 4 personnages du cercle via l'arbre de ressources de Penumbra.
   Nombre de fichiers, octets bruts, octets compressés. Ce chiffre pilote tout le
   dimensionnement.
3. **Enquête connectivité du cercle** : Windows 10 ou 11, FAI, type de NAT, présence d'IPv6,
   débit montant.

L'arithmétique à garder en tête : Mare faisait un upload vers un CDN puis N téléchargements. En
P2P, c'est N uploads depuis votre box. Un personnage de 500 Mo sur un uplink de 10 Mbit/s, c'est
7 minutes par pair, 35 minutes si cinq pairs arrivent ensemble. D'où le pré-téléchargement dès
que les deux pairs sont en ligne, et pas seulement quand ils se voient à portée de rendu.

### Jalon 1 : la boucle jeu, en local, sans réseau (2 semaines)

Un plugin minimal qui capture son propre personnage via Penumbra et Glamourer, écrit le
manifeste dans un fichier, et se l'applique via une collection temporaire. Aucun socket.

Il doit traverser sans crash : changement de zone, combat, GPose, cinématique, mort et
résurrection, changement de tenue rapide, rechargement à chaud du plugin, et déchargement propre.

Ce jalon lève les inconnues qui changeraient la conception : le thread requis par chaque appel
IPC, l'ordre exact d'application, le coût du recalcul, et **si Penumbra dépend de l'extension du
fichier cible**. Si oui, le nom du blob devient `<hash>.<ext>` et l'extension devient une donnée
fournie par le pair, ce qui a une conséquence de sécurité directe.

Livrable annexe : `RecordingModRepository`, qui journalise chaque appel IPC et sa réponse dans un
fichier rejouable sous Linux. C'est ce qui transforme une session en jeu en test de régression.

### Jalon 2 : faisabilité du transport (1 à 2 semaines)

`Linkpearl.Harness` minimal, sans crypto ni manifeste. Corpus de 300 Mo.

Mesures, sous Linux avec `tc netem` puis avec le simulateur interne :
- débit sur 1, 4, 8, 16, 24 et 32 canaux, à 5, 20, 60 et 150 ms de RTT, avec 0%, 1% et 3% de
  perte ;
- mémoire et CPU en réception à débit maximal, puisque c'est dans le processus du jeu ;
- blocs de 4, 16 et 64 KiB ;
- **ping FFXIV pendant un upload**, qui est le vrai critère social ;
- mode relais WebSocket, pour comparaison.

**Critère de passage : 8 Mo/s soutenus à 60 ms et 1% de perte, moins de 150 Mo de mémoire
résidente ajoutée, moins de 15% d'un cœur, et ping du jeu qui ne monte pas de plus de 30 ms.**

En parallèle, `Linkpearl.PunchProbe` et un rendez-vous sur un VPS, avec 5 à 10 personnes du
cercle en configurations variées (fibre, ADSL, 4G, CGNAT, IPv6 actif ou non, deux clients
derrière le même routeur). Mesurer la part de succès IPv6, de succès punch IPv4, le temps médian
d'établissement, et catégoriser les échecs.

### Jalon 3 : crypto et protocole (2 semaines)

`Core/Crypto` et `Core/Protocol` complets et testés, `docs/protocol.md` écrit, vecteurs figés,
**relecture externe de la construction**. Se termine par un handshake réel sur loopback.

### Jalon 4 : manifeste, cache, sûreté (1 à 2 semaines)

`Core/Manifest`, `Core/Cache`, `Core/Safety`, plus le harnais transférant un vrai corpus de mods
de bout en bout, et le mode `--hostile`.

Le répertoire et le quota sont des paramètres du noyau dès ce jalon, testés sous Linux
(déplacement de répertoire repris après interruption, éviction au passage sous le quota, passage
en lecture seule sous le seuil d'espace libre). Seuls les contrôles visuels attendent le
jalon 7.

### Jalon 5 : rendez-vous et relais (1 à 2 semaines)

`Linkpearl.Rendezvous` : signalisation par tickets tournants, réflexion d'adresse, relais
WebSocket plafonné, preuve de possession, anti-spam. Docker, déployable sur un VPS à quelques
euros.

### Jalon 6 : boucle de synchronisation (2 semaines)

`SyncEngine`, machine à états `Disconnected -> Rendezvous -> Punching -> Handshaking ->
Connected -> Visible -> Applied`, avec retour à `Connected` quand le pair sort du champ, sans
couper la session ni jeter le cache. Appariement par empreinte, debounce, application, nettoyage.
**Premier essai à deux joueurs réels.**

### Jalon 7 : interface (1 à 2 semaines)

Liste des pairs avec leur état, progression des transferts, pairage, révocation, pause, réglages
de cache (répertoire avec espace libre, quota avec occupation courante, purge globale ou par
pair), et la fenêtre de première utilisation qui explique honnêtement les deux risques :
les fichiers reçus d'un pair sont parsés par le jeu, et une connexion directe révèle votre
adresse IP.

Un échec de connexion doit **nommer sa cause probable** (NAT symétrique des deux côtés, bascule
sur le relais) et non afficher un « échec de connexion » inutile.

### Jalon 8 : robustesse et diffusion (2 semaines)

Nettoyage après crash, reprise de transfert par blobs déjà acquis, dépôt tiers, `repo.json`,
script de publication calqué sur `release-plugin.sh`.

**Hors v1 mais à ne pas rendre impossible** : ressources transitoires (animations, VFX, sons,
avec persistance par personnage et par job), objets possédés (monture, minion, pet, compagnon),
syncshells de groupe, Customize+ et Moodles via le bitset de capacités du handshake, budget VRAM
et triangles avec pause automatique des pairs trop lourds, transfert par delta.

---

## Fichiers de référence à relire avant de commencer

- `/home/yrapenne/Projects/FF14-JDR-Systeme-D10/EorzeaD10.Rules.Tests/EorzeaD10.Rules.Tests.csproj`
  le pattern d'inclusion de sources à reproduire, avec l'ajout des `.props` partagés
- `/home/yrapenne/Projects/FF14-JDR-Systeme-D10/EorzeaD10/EorzeaD10.csproj`
  SDK Dalamud 15, cible réelle `net10.0-windows7.0`
- `/home/yrapenne/Projects/FF14-JDR-Systeme-D10/CLAUDE.md`
  règles de thread, de style et de commits à transposer
- `/home/yrapenne/Projects/FF14-JDR-Systeme-D10/scripts/deploy-plugin-dev.sh`
  chaîne de compilation et de déploiement depuis WSL2
- `Penumbra.Api/IpcSubscribers/Temporary.cs` et `Glamourer.Api/IpcSubscribers/State.cs`
  signatures réelles des IPC, à vérifier au jalon 1

Le `CLAUDE.md` du nouveau dépôt doit porter en tête les trois règles dont la violation coûte le
plus cher :

1. `Core/` ne référence jamais Dalamud.
2. Tout transfert passe par plusieurs canaux et par blocs de 16 KiB, jamais par un gros message.
3. Toute donnée venant du réseau passe par `Core/Safety` avant d'atteindre un IPC ou le système
   de fichiers.

---

## Vérification

### Ce qui se teste sous Linux, sans le jeu : tout `Core/`

```sh
dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj
```

- `HandshakeTests` : nominal, mauvaise signature, clé inconnue, transcript modifié, rejeu,
  version incompatible, plus `HandshakeVectorTests` rejouant des vecteurs figés.
- `SecureChannelTests` : unicité des nonces sur 100 000 messages multi-canaux, tag altéré, rejeu,
  limite du compteur.
- `PairingCodeTests` : aller-retour, détection de faute par CRC, casse et caractères ambigus
  Crockford, version inconnue, plus un test de propriété sur entrées aléatoires.
- `GamePathPolicyTests` : corpus d'environ 200 chemins, moitié légitimes tirés d'un vrai dépôt de
  mods, moitié hostiles (traversée, séparateurs mixtes, UNC, unicode trompeur, extensions
  doublées).
- `ManifestTests` : canonicité (deux constructions équivalentes donnent le même hash), stabilité
  inter-versions, diff, plafonds.
- `FileSystemBlobStoreTests` : répertoire temporaire réel. Hash non conforme rejeté, écriture
  interrompue ne laissant aucun blob visible, écritures concurrentes du même hash, éviction
  respectant les épinglés, reconstruction de l'index.
- `ChannelPlanTests`, `RateLimiterTests` : déterministes avec `IClock` injectée.
- **`ArchitectureTests`** : échoue sur toute occurrence de `using Dalamud`, `using ImGui`,
  `using FFXIVClientStructs` ou `DateTime.Now` dans les sources de `Core/`. C'est ce qui empêche
  la dérive dans six mois.

### Harnais d'intégration à deux instances

```sh
dotnet run --project Linkpearl.Harness -- --corpus ./corpus --size 300M
dotnet run --project Linkpearl.Harness -- --loss 2 --latency 80 --jitter 20
dotnet run --project Linkpearl.Harness -- --resume
dotnet run --project Linkpearl.Harness -- --hostile
```

Deux `SyncEngine` complets dans un processus, chacun avec ses dépôts factices et son propre
`FileSystemBlobStore`, un rendez-vous en mémoire, et de vrais `NetManager` sur `127.0.0.1`.
Scénario complet : pairage, ticket, candidats, punch, handshake, offre de manifeste, transfert,
vérification que les deux `pathMap` appliqués sont identiques, coupure, reprise.

Le mode `--loss` est le test le plus rentable du projet : il expose le plafond de fenêtre de
LiteNetLib immédiatement, sans attendre un essai terrain. Le mode `--hostile` vérifie qu'un
manifeste malveillant est refusé sans planter et sans écrire hors du cache.

### Ce qui ne se teste qu'en jeu

IPC Penumbra et Glamourer, comportement du redessin, nettoyage au déchargement, changement de
zone, punch réel entre deux connexions distinctes. Liste de contrôle manuelle dans
`docs/manual-tests.md`, et `RecordingModRepository` pour rejouer les sessions sous Linux.

### Compilation

```sh
dotnet build Linkpearl/Linkpearl.csproj -c Release     # doit passer sans warning
./scripts/deploy-plugin-dev.sh                         # essai en jeu depuis WSL
```


## Inconnue levée le 22 septembre 2026

Penumbra applique sans difficulté un fichier nommé par son seul hash, sans extension.
Le nom d'un blob est donc uniquement son empreinte, recalculée localement, et l'extension
ne devient jamais une donnée fournie par le pair.
