# Où on en est, et par où reprendre

Écrit le 22 septembre 2026, en fin de première session. À lire en premier.

## Ce que le projet est devenu

Un successeur de Mare Synchronos en pair à pair. Les fichiers passent
directement d'un joueur à l'autre, chiffrés de bout en bout. Le seul service est
un rendez-vous minimal et auto-hébergeable, qui aide deux pairs à se trouver et
relaie quand le direct échoue.

**La conception a beaucoup bougé pendant la session**, et toujours dans le même
sens : de l'échange de secrets entre inconnus vers deux personnes debout l'une à
côté de l'autre dans le même jeu. Voir `pairage.md`, qui raconte les trois
itérations et pourquoi les deux premières étaient fausses.

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
5. **Les codes sont réservés aux groupes**, quand ils viendront. Un groupe n'a
   pas de nom de personnage, donc il lui faut un identifiant.
6. **Désactiver le plugin ne doit rien changer à l'apparence** du joueur. Les
   clients comparables ne le font pas, et un jour où je l'ai fait c'était un bug.

## État des jalons

| Jalon | État |
|---|---|
| 0, décider et mesurer | Fait pour l'utilisateur. Son cercle reste à interroger. |
| 1, boucle jeu locale | **Clos**, voir `jalon-1-resultats.md`. |
| 2, débit | **Mesuré**, voir `jalon-2-debit.md`. Le ping en jeu reste à mesurer. |
| 2 bis, traversée de NAT | NAT classé favorable. Test de paire réel en attente d'une seconde personne. |
| 3, crypto et protocole | **Clos**. Relecture externe toujours due, voir plus bas. |
| 4, cache et transfert | **Clos**, chaîne vérifiée de bout en bout sur des données réelles. |
| 5, rendez-vous et relais | **Clos**, déployé et éprouvé depuis l'internet. Le serveur vit désormais dans son propre dépôt. |
| 6, moteur de synchronisation | **Clos côté code**. Jamais éprouvé en jeu, voir ci-dessous. |
| 7, interface | Partiellement fait, en avance sur le plan. |

## Ce qui reste au jalon 6

Fait : carnet de pairs, empreintes, anti-rebond, appariement, boîtes aux lettres,
détection, demande et acceptation par interface, liaison LiteNetLib, réflexion
d'adresse, candidats, connecteur, session authentifiée, dialogue avec un pair.

Les trois pièces qui manquaient sont écrites : le moteur (`Core/Sync/SyncEngine.cs`),
l'applicateur distant (`Integration/RemoteApplicator.cs`, avec la décision dans
`Core/Sync/AppearancePlan.cs`) et le mode « faux pair » du harnais.

Le câblage dans `Plugin.cs` est fait : le moteur est construit avec le carnet, le
cache partagé, la socket unique et l'applicateur ; `LocalAppearance` construit
notre manifeste en tâche de fond depuis Penumbra ; `Framework.Update` bat la
socket ; un tic par seconde fait avancer le moteur en réutilisant l'instantané
de l'ObjectTable que la boucle d'interface prend déjà.

**Rien de tout cela n'a encore tourné en jeu.** C'est la seule chose qui
compte maintenant, et c'est aussi la seule que ni les tests ni le harnais ne
peuvent dire. Deux commandes ont été ajoutées pour cela : `/lpearl sync` dit où
en est chaque pair, `/lpearl rebuild` reconstruit l'apparence annoncée.

Ce qui reste à faire de mémoire d'ici là :

- **Rien ne détecte un changement de mods.** L'apparence se reconstruit au
  changement de personnage et sur `/lpearl rebuild`, pas quand l'utilisateur
  change de tenue. Il faut s'abonner aux événements de Penumbra, et faire passer
  la rafale par le `Debouncer` qui attend déjà dans `Core/Sync`.
- **L'éviction du cache ne connaît pas ce qui est à l'écran.** `EvictToAsync`
  prend un ensemble d'épinglés que personne ne lui donne : le moteur devrait lui
  passer les blobs des apparences posées, sinon le cache peut retirer sous les
  pieds ce qu'un pair porte.

### Ce que le faux pair a mesuré

Une apparence réelle de 405 Mo, 70 blobs, 81 chemins de jeu, en boucle locale,
sans limiteur :

| canaux | durée | débit |
|---|---|---|
| 1 | 123,3 s | 3,3 Mo/s |
| 2 | 70,0 s | 5,8 Mo/s |
| 4 | 45,5 s | 8,9 Mo/s |
| 8 | 31,8 s | 12,7 Mo/s |
| 16 | 26,1 s | 15,5 Mo/s |
| 24 | 83,2 s | 4,9 Mo/s, et un passage sans aboutir en cinq minutes |

Le gain s'inverse au-delà de seize, et le défaut par défaut est donc passé à
huit. Ce n'est plus le réseau qui décide à ce stade mais la localité des
lectures et des écritures, ce que le jalon 2 ne pouvait pas voir : il mesurait
un flux synthétique, pas soixante-dix fichiers ouverts de part et d'autre.

Avec le limiteur, qui part à 512 Kio/s et gagne 128 Kio toutes les deux
secondes, le même transfert prend 143 s. C'est lui qui décide alors, et c'est
voulu : personne ne veut voir son jeu ramer parce qu'une amie vient d'arriver.

## Défauts connus, non corrigés

- **L'interrogation de présence toutes les trois secondes est trop bavarde.**
  Environ 345 Mo par mois et par joueur, contre 20 avec une cadence de quinze
  secondes et seulement quand les joueurs visibles changent. Facteur 17 pour un
  confort invisible.
- **Le handshake SIGMA-I n'a pas été relu par quelqu'un d'autre.** C'est une
  condition de diffusion, pas une amélioration souhaitable. `protocol.md` existe
  pour cela.
- **Les trames de contrôle de LiteNetLib restent en clair**, donc falsifiables
  par qui connaît l'adresse. Déni de service, pas atteinte à la confidentialité.

## Ce qui attend l'utilisateur en jeu

- Mesurer le **ping de FFXIV pendant un transfert**. C'est le vrai critère du
  jalon 2, celui que le plan avait mal posé.
- Le **test de paire réel** entre deux réseaux, quand quelqu'un sera disponible.
- **Deux joueurs qui se voient**, qui est la preuve que le projet marche.

## Pièges appris à la dure

- Penumbra applique un fichier nommé par son seul hash : l'extension n'est jamais
  une donnée fournie par le pair.
- Passer une clé non nulle à `ApplyState` de Glamourer **verrouille** l'état,
  même sans le drapeau `Lock`. La documentation dit « to unlock or lock ».
- LiteNetLib ne garantit l'ordre qu'à l'intérieur d'un canal : un blob entier
  doit tenir sur un seul canal.
- Sa file d'envoi n'est pas bornée : sans contre-pression, un transfert alloue
  tout d'avance dans le processus du jeu.
- Sa fenêtre fiable est une constante de 64 paquets : le multi-canal est la
  condition d'existence du transfert, pas une optimisation.
- Le runtime .NET ne vérifie pas qu'un point public importé est sur la courbe.
- La réflexion d'adresse doit partir de la socket qui portera les liens.
