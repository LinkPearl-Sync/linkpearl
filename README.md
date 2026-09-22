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

## État

En cours de conception et de construction. Rien n'est utilisable pour l'instant.

La conception complète, les mesures qui la justifient et les jalons sont dans
[`docs/superpowers/specs/2026-09-22-linkpearl-design.md`](docs/superpowers/specs/2026-09-22-linkpearl-design.md).

## Périmètre de la première version

Apparence statique du personnage joueur, entre pairs appairés un à un.

Ne sont **pas** dans la première version, et c'est assumé : les ressources transitoires
(animations, VFX, sons), les objets possédés (monture, mascotte, familier), et les groupes.
