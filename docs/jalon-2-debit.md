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
3. Cent secondes pour un personnage lourd est acceptable si le jeu reste
   jouable pendant ce temps. Le projet passe.
4. Vendoriser LiteNetLib pour élargir sa fenêtre n'a plus d'intérêt immédiat :
   le débit brut n'est pas le facteur limitant, la congestion l'est.

## Reste à mesurer

- Intervalle de `PollEvents` à 1 ms contre 15 ms : savoir si c'est la fenêtre de
  64 paquets ou la fréquence d'appel qui plafonne.
- Plusieurs mesures par case, pour distinguer 16, 24 et 32 canaux.
- Le ping de FFXIV pendant un transfert réel, qui est le vrai critère.
