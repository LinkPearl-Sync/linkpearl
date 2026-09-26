# Le cercle ouvert : admettre des services sans geste humain

Écrit le 26 septembre 2026. Conception d'un sous-système, à lire après
`2026-09-22-federation-design.md`, dont elle amende trois décisions nommément
(voir [Ce que cette conception rouvre](#ce-que-cette-conception-rouvre)).

## Contexte

Aujourd'hui, un service de rendez-vous n'entre dans la liste d'un joueur que par
deux gestes humains : l'opérateur d'un annuaire approuve la candidature en
déplaçant une ligne, puis l'utilisateur coche la case. C'est ce qui protège le
pairage, puisqu'un service peut s'intercaler au premier contact
(`docs/protocol.md`, tableau des adversaires). Mais cela veut dire aussi qu'un
service monté par quelqu'un de bonne volonté ne sert à personne tant que deux
personnes n'ont pas agi, et que la charge reste sur le service par défaut.

L'idée vient de Tor : un relais se publie lui-même, des autorités le sondent,
la confiance se gagne avec le temps, et le client choisit tout seul dans une
liste signée. Tor peut se le permettre parce qu'un circuit traverse trois
relais dont aucun ne voit les deux bouts. Chez nous, un seul service tient la
boîte d'un joueur : transposer Tor tel quel ferait de n'importe quel VPS loué
cinq euros un intercepteur de pairages.

## Ce que la conception doit servir

Par ordre d'importance :

1. **Admettre sans approbation manuelle** un service neuf.
2. **Ne plus demander à l'utilisateur de cocher** : le client choisit seul dans
   une liste fiable.
3. **Réduire ce que chaque opérateur apprend de la présence** : qu'un service
   ne voie qu'une tranche du réseau et non tout le monde.

Plusieurs autorités qui votent (à la Tor) ne sont pas un but de cette
conception. Le format ne doit pas les empêcher.

## Décisions arrêtées

### Deux cercles, et la frontière existe déjà dans le protocole

Les boîtes se partagent en deux familles, selon ce dont elles se dérivent.

- Ce qui se dérive de **`nom@monde` seul** est calculable par n'importe qui : la
  boîte personnelle, donc les demandes et réponses de pairage, les défis et
  bienvenues des groupes privés, les boîtes d'admission. C'est là que passe une
  clé en clair, là qu'un service peut s'intercaler.
- Ce qui se dérive d'un **secret** (de paire ou de groupe) n'est calculable que
  par ceux qui le détiennent : boîtes de présence, jetons d'annonce, jeton de
  relais. Un service qui les tient peut faire échouer une connexion ou voir
  passer des métadonnées, jamais faire accepter une autre identité, puisque la
  clé est épinglée.

D'où deux cercles :

| | Cercle d'ancrage | Cercle ouvert |
|---|---|---|
| Composition | services distribués avec le plugin, plus ceux que l'utilisateur ajoute | services de la liste signée, sortis de probation |
| Entrée | un geste humain, comme aujourd'hui | automatique, après sonde et probation |
| Porte | tout ce qui dérive de `nom@monde` | tout ce qui dérive d'un secret, **pour un pair dont la clé est épinglée** |
| Pire cas d'un opérateur hostile | s'intercaler au premier contact | refuser le service, voir des métadonnées |

La phrase de `docs/pairage.md`, « pour un cercle qui héberge son propre
service, l'opérateur est l'un d'eux », reste vraie mot pour mot : seuls les
services d'ancrage voient passer une clé.

### Un membre de groupe non épinglé reste dans le cercle d'ancrage

La poignée de main SIGMA-I n'intègre pas le secret du groupe. Entre deux
membres qui ne se sont jamais vus, l'épinglage est une confiance au premier
contact (voir « Modèle de confiance pour les groupes » dans `docs/protocol.md`).
Un service ou un relais du cercle ouvert pourrait terminer la poignée de main de
chaque côté avec sa propre clé, et le TOFU l'épinglerait.

Tant que la clé d'un membre n'est pas épinglée dans le carnet de groupe, sa
présence, ses annonces et son relais passent donc par le cercle d'ancrage. Une
fois épinglée, il rejoint le cercle ouvert comme un pair ordinaire.

Mêler le secret de paire à la poignée de main lèverait cette restriction, et
fermerait l'interception partout. C'est un changement cryptographique du plugin,
hors de cette conception (voir [Plus tard](#plus-tard)).

### Un annuaire unique, qui fait autorité sur le cercle ouvert seulement

Le placement de la présence par hachage exige que tous les clients aient **la
même liste** : sinon Alice cherche Bob sur X pendant que Bob s'est installé sur
Y. Cela exclut les listes composées par chacun et les mesures faites par les
clients eux-mêmes. Il faut un document unique et signé.

Un seul annuaire signataire suffit pour commencer, celui du service du projet.
C'est acceptable parce que le cercle ouvert ne peut toucher aucune identité : si
la clé de l'annuaire est volée, l'attaquant peuple le cercle de services
hostiles qui peuvent refuser le service et observer des métadonnées, pas
usurper. S'il disparaît, les clients gardent leur dernière liste jusqu'à son
expiration, puis retombent sur le cercle d'ancrage : rien ne casse.

## Admission

### Candidature

Elle ne change pas sur le fil : `--announce-to <hôte>` envoie `DirectorySubmit`
(0x14) une fois au démarrage, puis referme. Les limites existantes restent (une
par adresse IP source et par heure, 256 en attente).

Ce qui change est le sort de la candidature chez un annuaire qui tient le rôle
d'autorité : elle n'attend plus que l'opérateur déplace une ligne, elle entre
dans la file des **candidats sondés**. Un annuaire sans ce rôle garde le
comportement actuel.

### Le rôle d'autorité

Désactivé par défaut, activé par `--directory-authority`. Il exige une clé de
signature, `directory.key` dans le répertoire d'état, engendrée au premier
démarrage avec ce drapeau et lisible par son seul propriétaire, comme
`admin.token`. La perdre oblige à publier une nouvelle version du plugin : ne
jamais l'écraser ni la régénérer, au même titre que `bans.json`.

### Sondes

Toutes les dix minutes, l'autorité ouvre une connexion vers chaque candidat et
chaque service listé, envoie `DirectoryQuery` (0x12) et attend une réponse bien
formée, quelle qu'elle soit, sous cinq secondes. Rien d'autre : pas de mesure
de débit, pas de test UDP, aucune donnée d'utilisateur.

C'est la seule connexion sortante d'un rendez-vous hors candidature, et seul
le rôle d'autorité l'ouvre.

L'historique des sondes (horodatage, succès) est persisté dans
`authority.json` pour qu'un redémarrage ne remette pas les probations à zéro.

### Probation

| Événement | Effet |
|---|---|
| Première sonde réussie | le candidat entre en probation |
| 7 jours de probation à au moins 95 % de sondes réussies | il entre dans la liste signée |
| 24 heures sans sonde réussie | il sort de la liste |
| Retour dans les 7 jours suivant sa sortie | il reprend sa place sans nouvelle probation |
| Retour plus tard | la probation recommence |
| Candidat jamais joignable pendant 7 jours | il est oublié |

### Contre l'afflux de services d'un même acteur

- Au plus **deux services par /24 en IPv4 ou par /48 en IPv6** dans la liste.
  Le sous-réseau se lit sur l'adresse résolue par la sonde. Un troisième reste
  en probation réussie, en attente d'une place.
- Au plus **cinq entrées par jour** dans la liste, toutes origines confondues,
  les plus anciennes probations d'abord.
- Le placement choisit ses deux services dans deux sous-réseaux différents
  (voir [Placement](#placement)).

Ces bornes n'empêchent pas un acteur patient et bien doté de peupler une part du
cercle. Elles bornent la vitesse et la concentration, ce qui suffit puisque le
pire cas reste le refus de service et l'observation de métadonnées.

### Veto de l'opérateur

La console gagne une action « écarter » sur un service listé ou candidat. Un
service écarté sort de la liste à la prochaine émission et ses candidatures
suivantes sont ignorées. L'admission se passe du geste humain, le retrait non.

## La liste signée

### Contenu

```
liste     = version(4) || emise(8) || expire(8) || nombre(2) || entree*
entree    = longueur(1) || adresse_utf8 || longueur(1) || libelle_utf8 || famille(8)
signature = identifiant_cle(8) || ECDSA-P256-SHA256(liste)(64, forme r||s)
document  = liste || nombre_signatures(1) || signature*
```

- `version` croît à chaque émission ; un client refuse une liste de version
  inférieure à celle qu'il détient.
- `emise` et `expire` sont des secondes Unix ; `expire` vaut `emise` plus sept
  jours.
- `identifiant_cle` vaut les huit premiers octets de `SHA-256` de la clé
  publique compressée. Il sert à trouver la clé, jamais à lui faire confiance :
  le client ne connaît que les clés inscrites dans le plugin.
- `famille` vaut les huit premiers octets de
  `SHA-256("linkpearl:family:v1" || préfixe)`, le préfixe étant le /24 ou le /48
  de l'adresse que la sonde a résolue. Le client ne résout rien lui-même, et
  l'empreinte évite de publier l'adresse IP à côté du nom.
- Les bornes d'adresse et de libellé sont celles de l'annuaire actuel
  (`MaxDirectoryAddressLength`, `MaxDirectoryLabelLength`).

**ECDSA P-256 et non Ed25519** : la bibliothèque standard .NET n'a pas Ed25519,
et le projet ne prend aucune dépendance cryptographique. P-256 est déjà la
courbe des identités.

La liste de signatures permet d'ajouter d'autres autorités plus tard : il
suffira alors d'exiger un seuil. En attendant, une signature valide d'une clé
connue suffit.

### Distribution

Deux trames nouvelles dans `RendezvousWire`, sur le modèle des pages de la
liste de bannissement : `ConsensusQuery` (demande d'une page) et `ConsensusPage`
(une page du document). Le document entier est vérifié une fois reconstitué.

Le client la demande à l'autorité toutes les six heures, la vérifie, et
conserve la dernière valide sur disque. Aucun autre service ne la relaie : un
rendez-vous ne propage toujours rien.

## Placement

Pour une paire (ou un couple de membres de groupe épinglés), chaque service `s`
de la liste reçoit un score :

```
score(s) = SHA-256("linkpearl:place:v1" || secret_de_paire || adresse_canonique(s))
```

`adresse_canonique` est la `RendezvousAddress` écrite en UTF-8, hôte en
minuscules et port toujours explicite (`rdv.exemple.ch:47900`), pour que deux
écritures d'un même service donnent le même score.

Les services sont triés par score décroissant (comparaison des 32 octets en gros
boutiste), et on retient **les deux premiers dont les `famille` diffèrent**.
C'est un hachage de rendez-vous (HRW) : quand la liste change, seules les paires
dont un service choisi est entré ou sorti changent de place.

**Aucune rotation dans le temps.** Chez Tor, les descripteurs de services
cachés tournent parce qu'un attaquant peut calculer leur place et se poster
dessus. Ici, la place dépend du secret de paire, que l'attaquant ignore : il ne
peut capturer que des paires prises au hasard, dont il ne sait rien. Faire
tourner le placement ne ferait qu'exposer chaque paire à davantage d'opérateurs.

Deux clients qui détiennent des versions différentes de la liste retombent
presque toujours sur au moins un service commun parmi leurs deux. Le repli
couvre le reste.

### Ce qui passe par les deux services choisis

- l'ouverture des boîtes de présence de la paire, et leur interrogation ;
- les annonces de connexion, en parallèle sur les deux ;
- le relais, qui reste désigné par l'appariement comme aujourd'hui.

### Repli sur le cercle d'ancrage

Le client repasse par le cercle d'ancrage, pour la paire concernée, quand :

- il ne détient aucune liste valide, ou qu'elle a expiré ;
- les deux services choisis sont injoignables ;
- aucune annonce n'a abouti à un appariement dans les dix secondes.

Le cercle ouvert est un gain, jamais une dépendance.

## Migration

**Phase 1, à la sortie.** La présence est ouverte **sur les deux cercles**, pour
que les clients d'avant continuent de voir ceux d'après. Les annonces partent
sur le cercle ouvert d'abord, sur l'ancrage au repli. Le gain de charge et de
relais est acquis ; celui de confidentialité non.

**Phase 2, quand les versions d'avant ont disparu** (les compteurs de version du
service le diront). La présence d'un pair épinglé ne s'ouvre plus que sur le
cercle ouvert. Les services d'ancrage cessent de voir la présence des paires
existantes, et ne voient plus que les pairages. Le passage est un réglage du
plugin, pas une nouvelle version du protocole.

`Configuration` gagne la dernière liste valide et un interrupteur « cercle
ouvert » activé par défaut. Rien d'autre ne migre : le carnet et les tickets ne
changent pas.

## Pannes, et ce qu'on en dit

| Situation | Effet | Ce que l'interface dit |
|---|---|---|
| L'autorité tombe | Les clients gardent leur liste sept jours | Rien |
| La liste expire | Tout repasse par le cercle d'ancrage | La liste des services le montre |
| Un service choisi tombe | L'autre suffit ; les deux, repli | Rien |
| Un service ouvert ment ou avale | La connexion échoue sur lui, aboutit ailleurs | Rien de plus qu'aujourd'hui |
| Clé de l'autorité volée | Cercle ouvert peuplé de services hostiles : refus, métadonnées | Réparé par une version du plugin qui change la clé |
| Signature invalide | La liste reçue est ignorée, la précédente reste | Journal seulement |

## Ce que cette conception rouvre

Trois décisions de `2026-09-22-federation-design.md`, à amender dans ce
document-là au moment de la mise en œuvre :

- « Hors de cette candidature, le serveur n'ouvre aucune connexion sortante »
  devient : **sauf le rôle d'autorité, pour sonder**.
- « Rien n'entre dans une liste sans un geste humain » devient : **rien n'entre
  dans le cercle d'ancrage sans un geste humain**.
- « Un maître est acceptable tant qu'il ne fait qu'informer » devient : **un
  maître peut faire autorité sur le cercle ouvert, qui ne touche aucune
  identité ; il ne fait qu'informer sur le cercle d'ancrage**.

La règle 2 du `CLAUDE.md` du rendez-vous (« le rendez-vous n'est pas une
autorité ») est précisée dans le même sens. Le README du rendez-vous et
`docs/protocol.md` décrivent les deux cercles.

## Vérification

### Dans le noyau, sous Linux

- lecture, écriture et vérification du document signé ; refus d'une signature
  invalide, d'une clé inconnue, d'une version régressive, d'un document expiré ;
- placement : deux services de familles différentes, stabilité quand un
  service sans rapport entre ou sort ;
- choix du cercle : un membre de groupe non épinglé reste dans l'ancrage ;
- repli sur chacune des trois conditions.

### Dans le service

- probation, sortie, retour et oubli, sous horloge simulée (`IClock`) ;
- les bornes par sous-réseau et par jour ;
- le veto ;
- la persistance de `authority.json` et de `directory.key` à travers un
  redémarrage.

### Vecteurs figés

Le document signé (avec une clé de test fixe et une signature fournie telle
quelle, ECDSA n'étant pas déterministe dans la bibliothèque standard) et les
scores de placement entrent dans `rendezvous-vectors.json`, identique à l'octet
près dans les deux dépôts.

### Dans le harnais

Une autorité, trois services ouverts, un service d'ancrage, et deux clients déjà
pairés. Ils doivent se retrouver par le cercle ouvert. On coupe les deux
services choisis pour leur paire : ils doivent se retrouver par l'ancrage.

## Plus tard

- **Lier la poignée de main au secret de paire ou de groupe**, ce qui fermerait
  l'interception au premier contact entre membres de groupe et lèverait leur
  restriction au cercle d'ancrage.
- **Plusieurs autorités** et un seuil de signatures.
- **Des miroirs** de la liste signée sur les services d'ancrage, si l'autorité
  devient un goulet.
