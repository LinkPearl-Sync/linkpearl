# Linkpearl

Synchronisation pair à pair de l'apparence moddée dans Final Fantasy XIV.

Deux joueurs qui se sont explicitement appairés se voient mutuellement avec leurs mods
Penumbra et Glamourer. Les fichiers passent directement de l'un à l'autre, chiffrés de bout
en bout. **Aucun serveur ne détient, ne stocke ni ne redistribue le moindre fichier de mod.**

La seule infrastructure est un service de rendez-vous, auto-hébergeable, qui aide deux pairs
à se trouver et sert de relais chiffré quand la connexion directe échoue. Il ne voit ni clé
publique, ni nom de personnage, ni manifeste, ni fichier : seulement des adresses IP et des
jetons opaques qui tournent toutes les dix minutes.

Ce service vit dans son propre dépôt,
[linkpearl-rendezvous](https://github.com/LinkPearl-Sync/linkpearl-rendezvous). Les trois
fichiers du format de fil y sont copiés littéralement, et `Linkpearl.Core.Tests/Fixtures/rendezvous-vectors.json`
est le même fichier des deux côtés : le premier qui touche à un octet du format casse son
propre test.

## État, à lire avant d'installer

**Ce plugin n'a jamais été éprouvé entre deux joueurs.** L'application de
l'apparence d'un pair, la connexion et le transfert n'ont tourné qu'au banc
d'essai. Ce qui a été vu en jeu se limite à la capture de sa propre apparence.

**Le handshake n'a pas été relu par quelqu'un d'autre.** C'est une construction
SIGMA-I écrite à la main sur P-256 et AES-256-GCM, documentée dans
[`docs/protocol.md`](docs/protocol.md) précisément pour pouvoir être relue sans
lire le code. Les vecteurs figés du dépôt ne valident que sa cohérence avec
lui-même : ils ont été produits par l'implémentation qu'ils testent, donc ils
attrapent une dérive de format, pas une erreur de conception. **Si vous savez
lire ce genre de chose, c'est la contribution la plus utile que vous puissiez
apporter à ce projet.**

**Si la connexion directe échoue, il n'y a pas de secours.** Le relais existe
côté service mais le plugin ne s'en sert pas encore. Deux joueurs derrière des
NAT peu coopératifs peuvent ne jamais se joindre.

Autrement dit : installez-le pour participer à un essai ou pour lire le code,
pas en attendant qu'il marche.

## Installation

Dans Dalamud, `/xlsettings` > Experimental > Custom Plugin Repositories, ajouter :

```
https://raw.githubusercontent.com/LinkPearl-Sync/linkpearl/main/repo.json
```

Puis installer Linkpearl depuis la liste des plugins. **Penumbra et Glamourer
sont requis.**

Vous et vos pairs devez être réglés sur un même service de rendez-vous, ce qui
est le cas par défaut. Vous pouvez en héberger un vous-même, voir
[linkpearl-rendezvous](https://github.com/LinkPearl-Sync/linkpearl-rendezvous).

La conception complète, les mesures qui la justifient et les jalons sont dans
[`docs/superpowers/specs/2026-09-22-linkpearl-design.md`](docs/superpowers/specs/2026-09-22-linkpearl-design.md).

## Périmètre de la première version

Apparence statique du personnage joueur, entre pairs appairés un à un.

Ne sont **pas** dans la première version, et c'est assumé : les ressources transitoires
(animations, VFX, sons), les objets possédés (monture, mascotte, familier), et les groupes.
