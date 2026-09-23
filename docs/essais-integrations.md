# Essais en jeu des intégrations

À deux personnages A (émetteur) et B (receveur), appairés, visibles l'un de
l'autre, les plugins voisins installés chez les deux sauf mention.

| # | Action chez A | Attendu chez B |
|---|---|---|
| 1 | Activer un profil Customize+ | Proportions de A appliquées |
| 2 | Changer le décalage SimpleHeels | Hauteur de A corrigée |
| 3 | Changer le titre Honorific | Titre affiché, sans redessin ni clignotement |
| 4 | Ajouter un moodle | Statut visible sur A, sans nom d'applicateur ni effet visuel |
| 5 | Renommer un familier (PetNicknames) | Surnom visible sur le familier de A |
| 6 | Mettre A en pause chez B | Les cinq disparaissent |
| 7 | Désactiver Honorific chez B, changer le titre chez A | Rien ne casse chez B, le reste suit |
| 8 | Réactiver Honorific chez B | Le titre de A revient seul |
| 9 | A sort du champ puis revient | Tout est reposé |
| 10 | Lire le journal Dalamud chez A et B | Aucun nom de personnage, aucun ContentId |

## Animations, VFX et sons

Chez A, un mod d'idle ou de pose assise, un mod de VFX et un mod de son actifs
dans Penumbra.

| # | Action | Attendu |
|---|---|---|
| 11 | A s'assoit avec sa pose moddée | B voit la pose moddée, pas celle du jeu |
| 12 | A se relève, change de zone, relance le jeu, sans se rasseoir ; B regarde A s'asseoir | La pose moddée est déjà là : A l'a retenue (`transients.json` dans le dossier du personnage de A) |
| 13 | A lance un emote ou une action à VFX moddé | B voit le VFX moddé |
| 14 | Chez B, barre du haut : barrer les animations | La pose de A redevient celle du jeu, sans clignotement ; rien n'est téléchargé |
| 15 | Débarrer | La pose moddée revient seule |
| 16 | Chez B, carnet, bouton des effets sur la ligne de A : décocher les sons | Sons de A coupés, ceux des autres pairs intacts ; le bouton reste accentué |
| 17 | A désactive son mod d'idle dans Penumbra | B revoit l'idle du jeu à la reconstruction suivante |
| 18 | A change de job, joue une animation de combat moddée | Elle n'est annoncée que pour ce job ; revue sous un second job, elle devient commune |
| 19 | Journal Dalamud chez A et B | Chemins de jeu et noms de fichier seulement, jamais un chemin local complet |
