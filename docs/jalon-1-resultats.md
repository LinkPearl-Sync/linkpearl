# Jalon 1 : la boucle jeu, en local. Résultats

Clos le 22 septembre 2026. Quatre inconnues levées en jeu, dont une qui supprime
un risque de sécurité et une qui a révélé un défaut de mon code.

## Ce qui a été vérifié

| Épreuve | Résultat |
|---|---|
| Capture de l'apparence via Penumbra et Glamourer | fonctionne |
| Application depuis le cache, blobs nommés par leur seul hash | fonctionne |
| `revert` explicite | rend le personnage à son état normal |
| Déchargement du plugin sans `revert` préalable | le personnage revient tout seul |

## Les quatre inconnues

### 1. Penumbra n'a pas besoin de l'extension d'origine

Un fichier nommé par son seul hash s'applique sans difficulté. **Le nom d'un blob
est donc uniquement une empreinte recalculée chez nous.** L'extension ne devient
jamais une donnée fournie par le pair : il n'y a rien à valider de ce côté, et
rien qui permette de faire écrire un fichier sous un nom choisi par l'autre.

La spec signalait ce point comme un risque de sécurité à lever. Il n'existe pas.

### 2. Le poids réel d'un personnage

60 fichiers, 298,1 Mo, 64 chemins de jeu. Manifeste de 22 Ko bruts, 12 Ko
compressés, conforme à l'estimation. Hachage des 298 Mo en 537 ms, soit environ
555 Mo/s : le hachage n'est pas sur le chemin critique.

### 3. Les échanges de fichier sont rares

Zéro sur ce personnage. Ne pas les porter en v1 ne coûte rien ici. À reconfirmer
sur d'autres personnages avant d'en faire une règle.

### 4. Ce que la v1 laisse de côté : rien, sur ce personnage

Après correction (voir plus bas), zéro ressource écartée. Les `.shpk` observés
étaient les shaders standard du jeu, non moddés.

## Deux défauts trouvés par l'épreuve en jeu

### Les ressources vanilla étaient comptées comme écartées

Penumbra rend aussi ce qui n'est pas moddé. La liste blanche d'extensions
s'appliquait avant que la nature du chemin réel ne soit déterminée, donc les
shaders standard du jeu étaient rapportés comme « écartés ». Ce compteur sera
montré à l'utilisateur : le gonfler de ressources vanilla ferait croire qu'il
manque quelque chose.

Le test d'origine utilisait un `.mdl` vanilla, dont l'extension est autorisée, et
ne pouvait donc pas attraper le cas.

### Le nettoyage ne survivait pas à un rechargement du plugin

`Dispose` ne nettoyait que si une collection était connue en mémoire. Un
rechargement perd cet état sans retirer la collection posée dans Penumbra : elle
restait affectée au personnage, dont la collection normale n'était plus active,
et plus rien ne savait la retirer. C'est ce qui laisse un personnage bloqué
jusqu'au redémarrage du jeu.

L'identifiant de collection est désormais écrit sur le disque, et le chargement
rattrape ce qu'une session précédente a laissé.

## Une correction de la spec

Le verrou Glamourer ne vient pas de la clé mais du drapeau `Lock`. La clé passée
seule sert à déverrouiller si besoin. On ne verrouille pas quand on s'applique à
soi-même, sans quoi l'utilisateur ne peut plus reprendre la main depuis
l'interface de Glamourer. Le verrou redeviendra nécessaire pour l'apparence d'un
pair, afin que l'automation du receveur ne l'écrase pas.

## Ce que cela change pour le jalon 2

**Le corpus de référence est de 298 Mo**, et non les 300 Mo synthétiques choisis
au hasard. À 60 ms de latence, un canal LiteNetLib unique plafonne à 1,5 Mo/s,
soit 3 min 20 pour ce volume. Le multi-canal n'est pas une optimisation.
