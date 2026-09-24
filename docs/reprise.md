# Où on en est, et par où reprendre

Écrit le 22 septembre 2026, en fin de première session. Mis à jour le 23, après
la première journée d'essais en jeu, et le 24 pour les groupes. À lire en
premier.

## Ce que le projet est devenu

Un successeur de Mare Synchronos en pair à pair. Les fichiers passent
directement d'un joueur à l'autre, chiffrés de bout en bout. Le seul service est
un rendez-vous minimal et auto-hébergeable, qui aide deux pairs à se trouver et
relaie quand le direct échoue.

**La conception a beaucoup bougé pendant la première session**, et toujours dans
le même sens : de l'échange de secrets entre inconnus vers deux personnes debout
l'une à côté de l'autre dans le même jeu. Voir `pairage.md`, qui raconte les
trois itérations et pourquoi les deux premières étaient fausses.

**Le 23, deux personnages se sont vus pour la première fois**, sur une même
machine : pairage par l'interface, apparence moddée posée chez l'autre, suivi
des changements de tenue.

## Les contraintes qui viennent de l'utilisateur, et qu'aucun code ne dit

Ce sont elles qui ont invalidé le plus de travail. À ne pas réinventer.

1. **Une apparence moyenne pèse environ 800 Mo**, pas les 300 mesurés sur le
   premier personnage. Tout le dimensionnement en découle.
2. **Les joueurs ne se parlent pas sur Discord.** Au mieux ils s'échangent
   quelque chose par message privé en jeu. Toute vérification hors du jeu ne se
   fera jamais.
3. **Tout doit passer par l'interface**, comme dans les clients comparables : on
   demande un pairage, l'autre reçoit une invite, il accepte ou refuse.
4. **Un utilisateur ne doit jamais voir une clé.** Il voit des noms de
   personnage. C'est l'identité qui l'intéresse.
5. **Les codes sont réservés aux groupes.** Un groupe n'a pas de nom de
   personnage, donc il lui faut un identifiant, qu'on s'envoie par /tell.
6. **Désactiver le plugin ne doit rien changer à l'apparence** du joueur. Les
   clients comparables ne le font pas, et un jour où je l'ai fait c'était un bug.
7. **Les joueurs visés font du jeu de rôle, pas du donjon.** Quelques
   millisecondes de ping leur coûtent moins qu'une tenue qui met des minutes à
   arriver : le limiteur d'envoi est désactivé par défaut, et « à l'époque de la
   fibre », un débit de quelques Mo/s n'est pas acceptable.
8. **Les références d'ergonomie sont Snowcloak et UmbraSync** : listes plutôt
   que cartes, « Réappliquer » au clic droit, progression visible sur le
   personnage. Ce qu'un joueur y connaît déjà, il doit le retrouver ici.
9. **Une identité doit survivre à une réinstallation** sans fouiller dans
   AppData : c'est la sauvegarde en un fichier, mot de passe facultatif.

## Ce qui vient ensuite, dans l'ordre voulu par l'utilisateur

1. **Les autres intégrations, comme le font les clients comparables** :
   Customize+, SimpleHeels, Honorific, Moodles et PetNicknames. Fait le 23
   septembre, voir `superpowers/specs/2026-09-23-integrations-design.md` ;
   à éprouver en jeu avec `essais-integrations.md`.
2. **Les animations, les VFX et les sons**, avec un moyen d'en bloquer la
   synchronisation : un réglage global rapide, et un réglage par pair. Fait le
   23 septembre, voir `superpowers/specs/2026-09-23-transitoires-design.md` ;
   à éprouver en jeu avec la seconde partie de `essais-integrations.md`.
3. **Les groupes**, en cours, voir `superpowers/specs/2026-09-24-groupes-design.md`.
   Incrément 1 livré (noyau). Incrément 2 livré le 24 septembre (groupes
   privés) : création, code, admission par mot de passe ou par validation d'un
   modérateur, politique signée et propagée, gouvernance, page Groupes,
   sauvegarde v2 ; protocole dans la section « Groupes privés » de
   `protocol.md`. Reste l'incrément 3, le groupe Public et l'application des
   listes de bannissement des services, puis les essais en jeu : créer,
   rejoindre dans les deux modes, exclure, dissoudre, à deux personnages.

## État des jalons

| Jalon | État |
|---|---|
| 0, décider et mesurer | Fait pour l'utilisateur. Son cercle reste à interroger. |
| 1, boucle jeu locale | **Clos**, voir `jalon-1-resultats.md`. |
| 2, débit | **Mesuré**, voir `jalon-2-debit.md`, puis dépassé le 23 (voir plus bas). Le ping en jeu reste à mesurer. |
| 2 bis, traversée de NAT | NAT classé favorable. Test de paire réel en attente d'une seconde personne. |
| 3, crypto et protocole | **Clos**. Relecture externe toujours due, voir plus bas. |
| 4, cache et transfert | **Clos**. Transfert par tronçons depuis le 23. |
| 5, rendez-vous et relais | **Clos**, déployé et éprouvé depuis l'internet. Le serveur vit dans son propre dépôt. |
| 6, moteur de synchronisation | **Éprouvé en jeu**, à deux personnages sur une même machine. |
| 7, interface | Bien avancé : listes, badges de transfert, barre de statut, sauvegarde. |

## Ce qui a changé le 23

- **Transfert par tronçons de 4 Mio**, répartis sur 32 canaux
  (`Core/Transfer/BlobSegments.cs`). Un blob n'a plus à tenir sur un seul
  canal : c'était la traîne de tout transfert.
- **Accusés LiteNetLib à la milliseconde et découverte du MTU**
  (`PeerLinkFactory`). Au défaut de 15 ms, la fenêtre fiable se libérait trop
  lentement, même en local.
- **Limiteur** : une marge absolue de 20 ms avant de reculer, sans laquelle un
  lien local s'effondrait au plancher ; et débrayable, débrayé par défaut.
- **Reconnexion** : une session qui tombe après avoir tenu se réannonce
  aussitôt, et l'attente d'un pair absent se compte depuis le début de
  l'annonce, ce qui tient bien 25 s d'annonce sur 30.
- **Apparence locale** : on écoute aussi `StateChanged` de Glamourer, on attend
  que le personnage soit entièrement chargé avant de lire ses ressources
  (`DrawReadiness`), et une demande de reconstruction n'est plus jamais perdue.
- **Sauvegarde de l'identité** en un fichier, et une identité illisible est
  désormais écartée au lieu d'être écrasée en silence.
- Interface : « Autour de vous » en listes, badges de transfert sous les pairs,
  symbole HQ dans la barre de statut et le titre.
- **Animations, VFX et sons.** L'émetteur les capte à leur chargement
  (`TransientCapture`, événement Penumbra), les retient par job
  (`TransientMemory`, `transients.json` du personnage), les re-résout à chaque
  construction et écarte les `.pap` qui animent des os que son squelette n'a
  pas (`PapSkeletonCheck`). Le receveur contrôle la forme des fichiers
  (`TransientFileCheck`, règles mesurées sur 5 109 `.pap`) et filtre avant tout
  téléchargement selon le réglage global (barre du haut) et celui du pair
  (bouton du carnet).
- **Course du handshake corrigée** : un premier message scellé arrivé avant la
  fin du handshake chez le répondeur était perdu, ce qui rendait les tests du
  moteur intermittents.

### Ce que le faux pair mesure maintenant

Une apparence réelle de 405 Mo, 70 blobs dont plusieurs de 85 Mo, le vrai
moteur des deux côtés, sans limiteur, un relais qui retarde les paquets :

| | 0 ms | 20 ms | 60 ms |
|---|---|---|---|
| avant le 23, 8 canaux | 12,5 Mo/s | 5,5 Mo/s | 2,0 Mo/s |
| maintenant, 32 canaux | 53,8 Mo/s | 33,0 Mo/s | 17,5 Mo/s |

Au-delà de 32 canaux, c'est instable : 48 canaux se sont effondrés à 3,6 Mo/s à
60 ms. Les mesures d'avant le 23, qui plafonnaient vers 16 canaux, étaient
faussées par un `/tmp` en mémoire saturé par le banc lui-même ; il nettoie
désormais derrière lui.

Commande : `dotnet run -c Release --project Linkpearl.Harness -- fakepeer
--channels 32 --no-limit --latency 20`. Le mode `endtoend` sert un blob à la
fois et ne mesure pas le moteur.

## Ce qui reste de mémoire

- **L'éviction du cache ne connaît pas ce qui est à l'écran.** `EvictToAsync`
  prend un ensemble d'épinglés que personne ne lui donne : le moteur devrait lui
  passer les blobs des apparences posées, sinon le cache peut retirer sous les
  pieds ce qu'un pair porte.
- **Un client d'avant les tronçons ne peut plus échanger** avec un client
  d'après. Le receveur le dit dans le journal, mais la version du protocole n'a
  pas été relevée : ses vecteurs figés servent à la relecture externe. De même,
  un client d'avant les intégrations refuse les manifestes v2 (« version de
  manifeste inconnue ») : décidé ainsi puisque les transferts étaient déjà
  incompatibles.
- **SimpleHeels garde un décalage reçu** jusqu'à ce qu'on le désenregistre, ce
  qui exige un personnage visible : un pair retiré hors de vue garde son
  décalage chez nous jusqu'au rechargement de SimpleHeels.
- **Les échanges de fichiers de Penumbra ne partent pas.** Le classement les
  reconnaît (`FileSwap`) mais le manifeste ne les porte pas : un mod de pose
  fait d'un simple échange vers une animation du jeu ne se synchronise pas.
- **Le contrôle de squelette n'a aucun test automatique** : il passe par le
  chargeur Havok du jeu. À éprouver en jeu avec une animation d'un squelette
  étendu.
- **Le rythme d'une milliseconde de LiteNetLib** reste à surveiller en jeu :
  rien n'a encore mesuré ce qu'il coûte au processeur.
- **Les groupes privés n'ont pas encore été éprouvés en jeu.** Tout ce qui
  décide est testé sous Linux ; l'interface et les boîtes d'admission sur un
  vrai service, non.
- **La dissolution n'est relayée que par le propriétaire** : un membre qu'il
  ne croise plus garde le groupe. L'interface lui dit de garder le groupe
  listé jusqu'à ce que les membres l'aient vu.
- **Un joueur qui a coupé la détection n'est pas trouvé par ses groupes.** La
  détection interroge les boîtes de présence ; si elle ne tourne pas, un membre
  en groupe ne sera pas composé, même si sa boîte répond.
- **Un service tiers resté à 16 boîtes par session refuse l'ouverture dès huit
  groupes**, et plus tôt au fil des fenêtres : c'est la limite de
  `MaxMailboxesPerSession` du rendez-vous. À relever avant la généralisation.

## Défauts connus, non corrigés

- **Le handshake SIGMA-I n'a pas été relu par quelqu'un d'autre.** C'est une
  condition de diffusion, pas une amélioration souhaitable. `protocol.md` existe
  pour cela.
- **Les trames de contrôle de LiteNetLib restent en clair**, donc falsifiables
  par qui connaît l'adresse. Déni de service, pas atteinte à la confidentialité.

## Ce qui attend l'utilisateur en jeu

- Mesurer le **ping de FFXIV pendant un transfert**, maintenant que le limiteur
  est débrayé par défaut.
- Le **test de paire réel** entre deux réseaux, quand quelqu'un sera disponible.
  Tout ce qui précède a été éprouvé sur une seule machine.
- Les **groupes privés**, à deux personnages : créer un groupe, le rejoindre
  par mot de passe puis par validation, exclure un membre, dissoudre.

## Pièges appris à la dure

- Penumbra applique un fichier nommé par son seul hash : l'extension n'est jamais
  une donnée fournie par le pair.
- Penumbra rend les ressources *chargées* : lues pendant un redessin, elles sont
  incomplètes. Vu en jeu : un fichier sur soixante-quatre, et le pair a reçu les
  objets de base sans aucun mod.
- Passer une clé non nulle à `ApplyState` de Glamourer **verrouille** l'état,
  même sans le drapeau `Lock`. La documentation dit « to unlock or lock ».
- `StateFinalized` de Glamourer ne se lève qu'à la fin d'un changement groupé :
  une retouche manuelle ne lève que `StateChanged`.
- Moodles transporte « Nom@Monde » et des GUID identiques d'un personnage à
  l'autre ; PetNicknames transporte nom, monde et ContentId. Les deux sont
  nettoyés à l'envoi et vérifiés à la réception.
- LiteNetLib ne garantit l'ordre qu'à l'intérieur d'un canal : un tronçon entier
  doit tenir sur un seul canal.
- Sa file d'envoi n'est pas bornée : sans contre-pression, un transfert alloue
  tout d'avance dans le processus du jeu.
- Sa fenêtre fiable est une constante de 64 paquets, libérée au rythme de
  `UpdateTime` : le multi-canal et un rythme rapide sont la condition
  d'existence du transfert, pas une optimisation.
- Un seuil de ping relatif seul ne tient pas sur un lien local : 2 ms qui
  passent à 4, c'est cent pour cent de plus.
- DPAPI lie l'identité au compte Windows : copier le dossier de configuration
  ne survit pas à une réinstallation du système.
- Le runtime .NET ne vérifie pas qu'un point public importé est sur la courbe.
- La réflexion d'adresse doit partir de la socket qui portera les liens.
- Sous WSL, `/tmp` vit en mémoire : un banc qui n'y nettoie pas fausse les
  mesures suivantes avant de tout bloquer.
