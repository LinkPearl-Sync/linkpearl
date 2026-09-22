# Fédérer les instances de Linkpearl

Écrit le 22 septembre 2026. Conception d'un sous-système, à lire après
`2026-09-22-linkpearl-design.md`, dont elle ne remet en cause aucune décision.

## Contexte

Aujourd'hui Linkpearl n'est pas fédéré. Tout est ancré sur un unique
`Configuration.RendezvousHost` :

- la **détection** ouvre et interroge les boîtes aux lettres sur ce serveur, donc
  deux joueurs réglés ailleurs ne se voient pas, même côte à côte en jeu ;
- **l'invitation** dépose et retire son ticket sur ce serveur, donc un ticket
  venu d'ailleurs ne peut pas être remis ;
- la **connexion** s'annonce sur `pair.RendezvousHost`, enregistré comme notre
  propre hôte au moment du pairage (`PairingService.cs:134`), donc deux pairs aux
  réglages différents s'annoncent chacun chez soi et ne se trouvent jamais.

Le carnet porte déjà un hôte **par pair** et le code d'invitation long porte déjà
un suffixe `@hôte` : la plomberie est à moitié posée, personne ne s'en sert.

## Ce que la fédération doit servir

Quatre motifs, tous retenus.

1. **Survivre à la perte d'un serveur.** Le VPS tombe, reçoit une lettre
   d'avocat, ou cesse d'être payé : personne ne perd ses pairs.
2. **Laisser chaque cercle héberger le sien.** Une compagnie libre monte son
   rendez-vous sans couper ses membres de leurs amis restés ailleurs.
3. **Répartir la charge.** À huit cents mégaoctets par personne, le relais coûte
   cher quand le direct échoue.
4. **Ne dépendre de personne.** Aucun opérateur ne doit pouvoir couper le réseau
   ni savoir qui y est en ligne dans son ensemble.

## Décisions arrêtées

### La fédération vit dans le client, les serveurs s'ignorent

Le plugin parle à plusieurs rendez-vous. Aucun serveur n'en interroge un autre,
ne relaie pour un autre, ni ne répond au nom d'un autre.

La seule exception, et elle est étroite : un serveur peut **se présenter** une
fois à un annuaire, dans un seul sens, sans rien attendre en retour et sans
qu'aucune confiance lui soit accordée de ce fait. Il ne demande rien, il dépose
son adresse dans une file que l'opérateur lira.

La raison n'est pas le confort, c'est une phrase de `docs/pairage.md` sur
laquelle repose tout le modèle de confiance :

> « Un opérateur de rendez-vous malveillant peut toujours s'intercaler. C'est
> assumé : pour un cercle qui héberge son propre service, l'opérateur est l'un
> d'eux. »

Une demande de pairage se dépose dans la boîte de la cible, donc qui contrôle
cette boîte peut répondre à sa place. Dans le modèle client, cette boîte est chez
l'opérateur que **la cible** a choisi, et la phrase tient mot pour mot. Si les
serveurs se relayaient, ton serveur demanderait à ses partenaires « qui a la
boîte d'Alice ? » et un partenaire hostile répondrait « moi » : la personne
capable de s'intercaler ne serait plus l'un du cercle, mais quelqu'un que ton
opérateur a ajouté un mardi sans te le dire.

S'y ajoute le coût évité : découverte des serveurs, listes de confiance,
prévention des boucles, limitation de débit inter-serveurs, versionnement d'un
second protocole. Pour un binaire de sept cents lignes.

### Le prix assumé : la détection s'évase

Interroger N serveurs fait connaître à N opérateurs qui se tient autour de toi,
là où un seul le savait. Ce n'est pas rattrapable techniquement, seulement
bornable : la liste par défaut reste courte, et c'est l'utilisateur qui la
compose. La contrepartie est qu'il choisit lui-même qui apprend quoi, au lieu que
son opérateur choisisse pour lui.

### Un maître est acceptable tant qu'il ne fait qu'informer

Un serveur peut publier la liste de ceux qu'il connaît. Il ne sait rien des
utilisateurs, ne voit passer aucune boîte, et **s'il disparaît rien ne casse** :
les clients gardent leur liste, les pairages continuent. N'importe qui peut
publier un annuaire concurrent.

Ce qui est refusé, et pourquoi : un **aiguilleur** qui saurait quel serveur
détient quelle boîte apprendrait qui est en ligne sur tout le réseau et
deviendrait le point de panne unique ; un **relais universel** y ajouterait le
pouvoir de couper qui il veut. Une **mise à jour poussée** donnerait à qui tient
le maître l'exécution de code sur tous les rendez-vous : une seule compromission
et le réseau entier est pris. L'annonce d'une version disponible reste
acceptable, elle ne porte qu'un numéro et un lien, jamais un binaire.

### Rien n'entre dans une liste sans un geste humain

L'annuaire propose, l'utilisateur coche. Un serveur peut se porter candidat
auprès d'un annuaire, mais sa candidature attend l'approbation de l'opérateur.
C'est ce qui empêche l'annuaire de devenir une autorité par accumulation.

## Ce que ce document ne traite pas

**La modération.** Une liste de bannissement, son format, son application et le
chemin de signalement font l'objet d'une conception séparée. Les décisions déjà
prises et à ne pas rediscuter : le bannissement porte sur le **personnage** et
non sur la clé, laquelle se régénère en une seconde ; il se distribue sous forme
de `argon2(nom@monde)`, vérifiable pour qui a le nom en main et hors de prix à
énumérer, afin qu'aucune liste publiée ne constitue un annuaire de joueurs
utilisant des mods ; il s'applique **dans le client** autant que dans le serveur,
sans quoi un opérateur complaisant suffirait à offrir un refuge. Aucun
signalement à l'éditeur du jeu n'est envisagé : les mods violent ses conditions,
donc signaler quelqu'un exposerait tous les utilisateurs en même temps que
l'auteur.

**La console d'administration.** Sous-système ultérieur. Cette conception doit
seulement ne pas lui fermer la porte, voir la dernière section.

## Modèle de données

### Une adresse devient une valeur du noyau

`Core/Transport/Rendezvous/RendezvousAddress`, un `readonly record struct` portant
hôte et port, qui se lit et s'écrit `hôte` ou `hôte:port`, le port par défaut
restant 47900.

Aujourd'hui le port est une variable globale collée à n'importe quel hôte, alors
que la documentation recommande déjà le 443 pour les réseaux restrictifs : deux
serveurs peuvent parfaitement écouter ailleurs.

### La configuration porte une liste

```
RendezvousEntry(RendezvousAddress Address, string Label, bool Enabled)
Configuration.Rendezvous : List<RendezvousEntry>
```

Le défaut reste le serveur actuel, seul dans la liste. `Configuration.Version`
passe de 1 à 2 et la migration construit la liste depuis les anciens champs.

### Le carnet garde une liste ordonnée par pair

```
PairRecord.Rendezvous : IReadOnlyList<RendezvousAddress>
```

En remplacement de `RendezvousHost`. C'est ce qui fait survivre un pairage à la
mort d'un serveur. Au pairage, la liste vaut *[le lieu d'où vient l'invitation,
notre propre chez-nous]*, donc les deux côtés finissent avec le même ensemble.

### Le ticket d'invitation porte son lieu

Les douze caractères restent, suivis de `@hôte[:port]`, la forme que le code long
emploie déjà. Sans suffixe, on retombe sur le premier serveur de la liste, donc
les tickets existants continuent de fonctionner. Celui qui colle voit où le
ticket pointe avant de le remettre, et c'est là qu'il décide s'il fait confiance
à ce serveur.

## Les flux

### Présence et détection

`PresenceService` tient aujourd'hui une connexion unique, et c'est elle qui vaut
présence : la fermer déclare l'absence. Il en tiendra une par serveur activé,
chacune ouvrant nos boîtes et recevant les demandes poussées.

Un joueur est détecté si **au moins un** serveur reconnaît sa boîte, l'union se
faisant sur ceux qui répondent. Un serveur en panne sort de la ronde avec son
propre compte à rebours de reprise, et ne la bloque pas.

Les demandes de pairage arrivent sur le serveur choisi par l'expéditeur et sont
versées dans une file commune, dédoublonnées sur la clé du demandeur et le nonce :
un expéditeur qui dépose partout ne doit pas produire deux invites.

**La cadence de présence passe de trois à quinze secondes, et n'interroge que
lorsque les joueurs visibles changent.** Cette dette était déjà inscrite dans
`docs/reprise.md` ; la fédération la rend obligatoire, puisque 345 Mo par mois et
par joueur deviendraient un gigaoctet sur trois serveurs.

### Connexion

Pour chaque pair, on s'annonce **en parallèle sur tous les lieux de sa liste**.

La séquence paraît plus simple mais introduit une course perdue d'avance : nous
sur le premier serveur pendant cinq secondes, l'autre sur le second au même
moment, et nous nous manquons alors que nous sommes tous deux en ligne. En
parallèle, le premier serveur qui apparie gagne et les autres tentatives sont
abandonnées.

L'appariement désigne du même coup le serveur qui servira de relais si le direct
échoue, puisque c'est celui que les deux ont atteint.

### Annuaire

Deux trames nouvelles dans `RendezvousWire`, `DirectoryQuery` (0x12) et
`DirectoryList` (0x13) : une demande vide, et une réponse portant jusqu'à
soixante-quatre entrées, chacune une adresse et un libellé, le tout dans la
limite de trame existante de 64 Kio.

Côté serveur, la liste vient de **sa propre configuration**, un fichier écrit par
son opérateur et **relu à chaud** : ajouter un pair doit être une ligne écrite,
pas un redémarrage, sans quoi personne ne le fera.

Côté client, le bouton « découvrir » montre ce qui revient et **n'ajoute rien
tout seul**.

Le libellé vient du réseau : borné en longueur, et normalisé par `Glyphs.Safe`
avant affichage, comme les noms de pairs.

### Candidature d'un serveur

Hors de cette candidature, le serveur n'ouvre aucune connexion sortante : il ne
consulte jamais un autre annuaire, ne vérifie jamais un autre serveur, et ne
propage jamais ce qu'il a reçu. La soumission est un drapeau,
`--announce-to <hôte>`, qui envoie une trame `DirectorySubmit` (0x14) une fois au
démarrage, puis referme.

Côté annuaire, l'entrée tombe dans une file d'attente **persistée sur disque**,
pour qu'une seule candidature suffise et qu'un redémarrage ne la perde pas.
L'opérateur approuve en déplaçant la ligne vers son fichier de pairs.

Limites : une candidature par **adresse IP source** et par heure, deux cent
cinquante-six en attente au maximum, les plus anciennes évincées. Une adresse
déjà présente dans le fichier de pairs est ignorée sans entrer dans la file.

## Pannes, et ce qu'on en dit

| Situation | Effet | Ce que l'interface dit |
|---|---|---|
| Un serveur tombe | Sa session prend son compte à rebours, l'union se fait sur les autres | Rien, sauf dans la liste des serveurs |
| Tous tombent | Plus de détection ni de nouveau pairage | La barre d'état le dit |
| Un serveur ment sur une boîte | Détection fausse, le pairage n'aboutit pas | Rien de plus qu'aujourd'hui |
| Un serveur propose un serveur hostile | Rien n'est ajouté sans la case cochée | L'ajout est un geste, pas un effet |
| Aucun lieu commun joignable pour un pair | Le pair reste injoignable | **« aucun lieu de rendez-vous commun »**, et non « hors ligne » |

La dernière ligne mérite son message propre : dire « hors ligne » enverrait
l'utilisateur attendre pour rien, alors que la réparation est d'ajouter un
serveur ou de se repairer.

## Migration

Personne ne perd un réglage ni un pair, et rien n'exige d'action.

- `Configuration` version 1 vers 2 : l'hôte unique devient une liste d'une entrée.
- `PairBookStore` lit encore `RendezvousHost` et le convertit en liste.
- Un ticket sans suffixe retombe sur le premier serveur configuré.

## Vérification

### Sous Linux, dans le noyau

- lecture et écriture d'une `RendezvousAddress`, avec et sans port, et refus des
  formes malformées ;
- les deux migrations, depuis un fichier de la version précédente ;
- le ticket d'invitation avec et sans suffixe ;
- l'aller-retour des trames d'annuaire, et leurs refus : trop d'entrées, libellé
  démesuré, adresse illisible ;
- l'union de la détection sur plusieurs serveurs dont un muet ;
- l'annonce parallèle qui retient le premier appariement et abandonne les autres.

### Vecteurs figés

Les trames nouvelles entrent dans `rendezvous-vectors.json`, identique à l'octet
près dans les deux dépôts, et les fichiers de protocole sont recopiés dans
`linkpearl-rendezvous`.

### Dans le harnais

**Deux rendez-vous et deux clients dont les listes de serveurs ne se recouvrent
que partiellement**, qui doivent malgré tout se trouver et transférer. C'est le
seul test qui dise que la fédération marche.

## Ce que cette conception laisse possible

La console d'administration viendra plus tard. Pour qu'elle ne coûte alors qu'une
trame et non une réécriture, le serveur tient dès maintenant, comme état interne,
les compteurs qu'elle voudra lire : boîtes ouvertes, annonces en cours,
appariements réussis, débit de relais, pairs de l'annuaire, candidatures en
attente.
