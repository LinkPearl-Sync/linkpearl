# Jalon 2 : débit de LiteNetLib. Mesures

Mesuré le 22 septembre 2026. Corpus de 32 Mo, blocs applicatifs de 16 Kio,
`PollEvents` toutes les 15 ms (la fréquence d'affichage du jeu). Deux
`NetManager` séparés par un relais UDP qui applique retard, gigue et perte.

Contrôle du banc : à latence nulle et 32 canaux, 29,85 Mo/s. Les mesures
ci-dessous sont donc très en dessous du plafond du banc, ce sont bien des
limites de LiteNetLib.

## Débit applicatif, en Mo/s

| Canaux | 20 ms 0 % | 60 ms 0 % | 150 ms 0 % | 20 ms 1 % | 60 ms 1 % | 150 ms 1 % |
|---|---|---|---|---|---|---|
| 1  | | 0,41 | | | | |
| 8  | 7,14 | 3,19 | 1,42 | 4,65 | 1,68 | 0,78 |
| 16 | 12,86 | 6,34 | 2,68 | 8,74 | 3,67 | 1,50 |
| 24 | 14,54 | 7,82 | 3,89 | 9,12 | 3,00 | 2,27 |
| 32 | 15,34 | 5,58 | 4,48 | 7,33 | 4,65 | 2,27 |

**Une seule mesure par case, et la variance est visible.** Ces chiffres tranchent
l'ordre de grandeur, pas la différence entre 24 et 32 canaux.

## Ce que cela établit

**Le multi-canal était bien la condition d'existence du projet.** Un canal unique
à 60 ms donne 0,41 Mo/s, soit douze minutes pour un personnage de 298 Mo. Huit
canaux multiplient par huit. Le gain est quasi linéaire jusqu'à seize canaux,
puis s'aplatit.

**Le critère de passage que le plan fixait, 8 Mo/s à 60 ms et 1 % de perte,
n'est pas atteint.** On plafonne entre 3 et 4,7 Mo/s, soit environ 100 secondes
pour 298 Mo, et 131 secondes dans le pire cas mesuré (150 ms, 1 %).

## Mais le critère était mal posé

Les pings relevés pendant les transferts montent à 145 ms quand la latence réelle
est de 60, et jusqu'à 343 ms sur une mesure à 24 canaux. On remplit les files
d'attente, exactement comme la revue l'annonçait pour une bibliothèque sans
contrôle de congestion.

En jeu, cela veut dire le ping de FFXIV qui s'effondre pendant qu'un pair
télécharge nos textures. **La bonne question n'est donc pas « combien de Mo/s »
mais « quel débit sans dégrader le ping du jeu ».**

Conséquences :

1. Le limiteur AIMD n'est pas une optimisation à ajouter plus tard : c'est lui
   qui définira le débit réel, et il le fixera sous les chiffres ci-dessus.
2. Le critère du jalon devient une contrainte sur le ping du jeu, à mesurer en
   jeu et non sur le banc.
3. Cent secondes pour un personnage de 298 Mo est acceptable si le jeu reste
   jouable pendant ce temps.

## Révision : 298 Mo n'est pas représentatif

Le personnage mesuré est un cas favorable. D'après l'observation de
l'utilisateur, **la moyenne se situe plutôt autour de 800 Mo**. Cela change les
conclusions, et pas à la marge.

| Débit | 298 Mo | 800 Mo |
|---|---|---|
| 3,00 Mo/s (60 ms, 1 %) | 1 min 40 | **4 min 27** |
| 2,27 Mo/s (150 ms, 1 %) | 2 min 11 | **5 min 52** |

Et le goulot change de nature selon la ligne de l'émetteur :

- **Sur une ligne à faible débit montant** (ADSL, câble à 10 Mbit/s), c'est le
  lien qui domine : 800 Mo demandent environ 11 minutes, quel que soit le
  protocole. LiteNetLib n'y est pour rien.
- **Sur une fibre symétrique**, le lien pourrait passer 800 Mo en une vingtaine
  de secondes, mais le plafond mesuré de 3 à 4,7 Mo/s (soit 25 à 37 Mbit/s)
  devient la limite. **Là, LiteNetLib coûte plusieurs minutes.**

Conséquences révisées :

4. Vendoriser LiteNetLib pour élargir sa fenêtre redevient pertinent, pour les
   utilisateurs en fibre chez qui c'est la bibliothèque et non le lien qui
   plafonne. À décider après la mesure du ping en jeu.
5. **Le pré-téléchargement dès que les deux pairs sont en ligne, et non quand
   ils se voient, cesse d'être un confort.** À cinq minutes par pair, attendre
   la mise à portée rendrait la synchronisation inutilisable.
6. La déduplication entre pairs prend une importance qu'elle n'avait pas : un
   cercle partage souvent les mêmes corps et les mêmes peaux de base, et ce qui
   est déjà en cache ne coûte rien.
7. L'arithmétique du lien montant devient le vrai sujet : cinq pairs qui
   arrivent ensemble, c'est cinq fois 800 Mo à émettre depuis une seule box.

## Reste à mesurer

- Intervalle de `PollEvents` à 1 ms contre 15 ms : savoir si c'est la fenêtre de
  64 paquets ou la fréquence d'appel qui plafonne.
- Plusieurs mesures par case, pour distinguer 16, 24 et 32 canaux.
- Le ping de FFXIV pendant un transfert réel, qui est le vrai critère.
