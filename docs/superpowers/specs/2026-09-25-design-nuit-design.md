# Refonte visuelle : la nuit du site

Écrit le 25 septembre 2026. Branche `design`, partie de `worktree-groupes-noyau`.

## Pourquoi

L'interface a grandi page par page. Chaque écran a ses cases ImGui brutes, ses
en-têtes repliables gris et ses espacements au jugé ; le bloc Public en était
l'exemple le plus visible. Et le plugin ne ressemble ni à son site ni à son logo :
ardoise grise et nacre d'un côté, nuit marine, halo bleu et pompons orange de
l'autre. Un joueur qui installe depuis
[linkpearl-sync.github.io](https://linkpearl-sync.github.io) ne reconnaît pas ce
qu'il vient d'installer.

## Ce que l'utilisateur a choisi

Choix faits sur maquettes, pendant la conception :

- **Le site complet**, polices comprises : Fredoka pour les titres, Nunito pour le
  texte.
- **L'ambiance « nuit du site »** : fond en dégradé avec le halo du haut, cartes
  translucides, bords très ronds, boutons en pilule, halo autour de ce qui est
  actif. Préférée à une nuit à plat et à une ardoise bleutée.
- **Le logo** dans la barre de titre, en pastille ronde, et à la place de l'icône
  sur les états vides. Pas en tête de la barre latérale.
- **Un interrupteur à droite**, curseur en perle, pour tout réglage oui/non. Ni
  case restylée, ni pilule Activé/Désactivé.
- Le périmètre : harmoniser toutes les pages, poser le logo, et traiter aussi les
  fenêtres annexes. **La structure ne change pas** : barre latérale, barre de
  titre et barre d'état gardent leur rôle et leur place.

## Palette

`Theme.cs` garde les noms de ses jetons et change leurs valeurs : les 27 fichiers
qui y puisent suivent sans retouche. Les couleurs du site viennent de sa feuille
de style (`--deep`, `--field`, `--glow`, `--pom`, `--ink`, `--mute`, `--faint`,
`--line`).

| Jeton | Valeur | Origine |
|---|---|---|
| `BgSunken` | `#040B26` | `--field` |
| `BgBase` | `#07123A` | `--deep` |
| `BgSidebar` | `#060F33` | barres, un cran sous le fond |
| `BgSurface` | `#131E44` | blanc à 5 % sur `--deep`, le fond des cartes du site |
| `BgRaised` | `#1D274C` | blanc à 9 % |
| `BgHover` | `#2A3356` | blanc à 14 % |
| `Accent` | `#5FB4FF` | `--glow` |
| `AccentHover` | `#8FCBFF` | |
| `AccentActive` | `#3D8FE0` | |
| `AccentMuted` | `#173067` | halo à 18 % sur `--deep` |
| `Action` (nouveau) | `#FFA45C` | `--pom` |
| `TextOnAction` (nouveau) | `#2A1300` | le texte du bouton « Copier » du site |
| `Text` | `#EAF1FF` | `--ink` |
| `TextMuted` | `#A9B8E6` | `--mute` |
| `TextFaint` | `#7F8DC0` | `--faint` |
| `Link` | `#8FD0FF` | les étoiles du site |
| `Online` | `#6FE0B0` | |
| `Idle` | `#F5D06F` | jaune, distinct de l'orange d'action |
| `Danger` | `#FF6B7D` | |
| `Border` | `#253965` | `--line`, soit `rgba(143,196,255,.22)` sur `--deep` |
| `BorderSoft` | `#172752` | la même à 12 % |
| `BorderLight` | `#37507F` | la même à 35 % |
| `Pearl*` (nouveaux) | `#FFFFFF`, `#BFE4FF`, `#7A9CFF`, `#C79BFF` | le dégradé des pastilles du site |

Les valeurs composées (fond des cartes, bordures) sont précalculées en opaque,
sauf là où une surface doit laisser voir le halo du fond : les cartes de premier
niveau se peignent en blanc à 5 % par transparence, comme sur le site.

**L'orange est réservé à l'action principale d'un écran**, une seule, comme le
bouton « Copier » du site. Aujourd'hui `BtnTone.Primary` sert à la fois à
« l'action à faire » et à « l'option choisie » (mode d'admission, effets) : les
deux se séparent. `BtnTone.Action` est l'orange ; `BtnTone.Selected`, teinté
halo avec un liseré `Accent`, marque ce qui est choisi. `Primary` disparaît, et
chacun de ses 14 usages est reclassé dans l'un ou l'autre.

Les statuts gardent leur sens : vert posé, jaune en cours ou en pause, rouge
bloqué ou danger.

## Polices

`Fonts.cs` garde ses quatre poignées et change de fichiers :

| Poignée | Aujourd'hui | Demain |
|---|---|---|
| `Title`, `H2` | Inter SemiBold | Fredoka SemiBold, Inter SemiBold en secours |
| `Body`, `Small` | Inter Regular | Nunito Regular, Inter Regular en secours |

Aucun gras au fil du texte n'existe aujourd'hui : Inter SemiBold ne sert qu'aux
titres. Nunito Bold n'est donc pas embarqué.

- **Fichiers statiques**, pas variables : l'atlas ImGui ne choisit pas la graisse
  d'une police variable. Google Fonts ne publie que les fichiers variables ; les
  instances statiques sont tirées par `scripts/generer-polices.sh`, avec
  fonttools 4.66.0, depuis `google/fonts` au commit `23e54b5`. Le script est ce
  qui rend les fichiers embarqués reproductibles.
- Licence OFL : les fichiers sont embarqués comme Inter l'est, avec leur
  `OFL.txt` à côté dans `Assets/Fonts/`.
- **Inter reste, fusionné derrière**, comme police de secours. Mesuré le 25
  septembre sur les chaînes de l'interface : Nunito n'a ni `→`, ni `◆`, ni `◇` ;
  Fredoka n'a pas non plus `≈`, et ne couvre que 10 caractères sur 128 du latin
  étendu A, qu'un nom de groupe peut porter. À la fusion, ImGui garde le premier
  glyphe venu : Nunito et Fredoka dessinent tout ce qu'elles ont, Inter le reste.
- **Les mêmes plages de glyphes** qu'aujourd'hui. Le script de génération
  vérifie que chaque caractère des chaînes de l'interface existe dans la police
  principale ou dans Inter, et échoue sinon.
- FontAwesome reste fusionné dans le corps de texte.
- La tolérance à l'échec ne change pas : si les nouveaux fichiers ne se chargent
  pas, la police de Dalamud prend le relais.

## Métriques

| Jeton | Aujourd'hui | Demain | Pourquoi |
|---|---|---|---|
| `RadiusWindow` | 10 | 12 | |
| `RadiusCard` | 8 | 14 | le site a 18, qu'ImGui adoucit mal en petite taille |
| `RadiusFrame` | 6 | 6 | champs de saisie, boutons d'icône carrés |
| pilule (nouveau) | | moitié de la hauteur du cadre | boutons à libellé, interrupteurs |

Les espacements (`GapXs` à `GapXl`, rembourrages) ne changent pas : ce ne sont
pas leurs valeurs qui posent problème, c'est leur usage irrégulier.

## Surfaces

`Surface` gagne trois primitives, toutes dessinées sur la draw list :

- **`NightBackground`** : un dégradé vertical `#0C1E5C` vers `#07123A`, puis le
  halo du haut en une dizaine d'ellipses concentriques à alpha décroissant
  (centre `#1D3B9A`). ImGui n'a pas de dégradé radial : c'est l'approximation.
  `ThemedWindow` le peint sous chaque fenêtre du plugin.
- **`Glow`** : trois contours arrondis qui s'élargissent en s'estompant, en
  `Accent`. Carte active, entrée active de la barre latérale, interrupteur
  allumé.
- **`Pearl`** : cercles superposés (bord lavande, corps bleu, reflet blanc en haut
  à gauche) et un léger halo. Remplace les points de statut neutres et sert de
  curseur à l'interrupteur allumé.

## Composants

- **`Toggle`** (nouveau) : `Toggle.Draw(label, ref value, id, hint)`. Libellé à
  gauche, interrupteur calé à droite au bord des actions de ligne
  (`Card.RightInset`). Éteint : fond translucide, curseur `TextFaint`. Allumé :
  fond teinté halo, `Glow`, curseur `Pearl`. Toute la rangée est cliquable, avec
  le curseur main. L'explication éventuelle vient dessous, en `TextFaint`.
- **`EffectsPicker`** (nouveau) : Animations, VFX et Sons en trois boutons
  `Selected` ou `Secondary`, avec infobulle d'état. Sert à la carte Public, à la
  popup d'effets d'un pair et à celle d'un membre de groupe.
- **`Fold`** (nouveau) : en-tête repliable, chevron, titre, compteur, sans le fond
  gris d'ImGui. Remplace les `CollapsingHeader` d'Autour de vous et de Pairs, et
  le bouton « Joueurs rencontrés » de Public.
- **`Btn`** : tons `Action`, `Selected`, `Secondary`, `Ghost`, `Danger`,
  `Success` ; boutons à libellé en pilule ; `Btn.Icon` reste carré arrondi, pour
  ne pas faire des ovales dans une ligne.
- **`Card`** : fond blanc à 5 %, bordure `Border`, `RadiusCard`. Le paramètre
  `accent` ne dessine plus une barre à gauche mais un `Glow` autour. Les
  sous-cartes de gestion restent un cran plus sombres.
- **`Feedback.EmptyState`** : accepte le logo à la place de l'icône.
- **`Text`** : `Title` et `H2` en Fredoka. Les titres de section prennent le style
  des libellés du site : petites capitales espacées, `TextFaint`.

Le logo est embarqué une fois (`Assets/Images/logo.png`, copie de
`Plugin_Logo.png`, lui-même identique à celui du site) et chargé comme la
bannière de l'onboarding, par `Textures.GetFromManifestResource`.

## Les écrans

Chaque passage suit la même liste : aucun widget ImGui brut (`Checkbox`,
`CollapsingHeader`, `Button`), aucune couleur en dur hors de `Theme`, des
`SameLine` à espacement explicite, une action orange au plus.

- **Le cadre** : barre de titre avec le logo rond et « LinkPearl » en Fredoka ;
  entrée active de la barre latérale en halo ; perle de connexion dans la barre
  d'état ; `NightBackground` sous chaque fenêtre.
- **Autour de vous** : `Fold`, logo sur l'état vide. Pas d'orange : aucune action
  n'y domine.
- **Pairs** : `Fold`, `EffectsPicker` dans la popup, logo sur l'état vide.
- **Groupes et Public** : orange sur « Rejoindre un groupe » ; `Selected` pour le
  mode d'admission ; `EffectsPicker` pour les membres et pour Public ; `Fold` ;
  carte Public en `Glow` quand elle est activée.
- **Demandes** : orange sur « Accepter », logo sur l'état vide.
- **Réglages** : `Toggle` pour les quatre cases à libellé, et un interrupteur
  seul (`Toggle.Switch`) pour les deux sans libellé : service actif, service
  proposé par un annuaire.
- **Onboarding** : couleurs en dur de `OnboardingArt` sur les jetons, points de
  progression en perles.
- **Rejoindre ou créer un groupe** : orange sur le bouton qui valide.
- **Notifications de demande** : orange sur « Accepter ». Leur fenêtre n'a pas de
  fond (`NoBackground`) et flotte sur le décor du jeu : leurs cartes restent
  opaques, en `BgSurface`, là où les autres sont translucides.
- **Badges de transfert en jeu** : pilule marine, texte `Text`, barre de
  progression `Accent`. Ils doivent se lire sur n'importe quel décor du jeu, donc
  le fond reste opaque à 85 % au moins.
- **Glyphes des noms** : « Disponible » en `Action`, « Demande en cours » en
  `Accent`, « Membre de groupe » en lavande de la perle.

## Ordre de livraison

Un commit et un déploiement par étape.

1. **Essai des points risqués** : polices, palette, `NightBackground`, `Glow`,
   `Pearl`. La palette étant globale, l'essai porte sur tout le plugin d'un coup,
   et la carte Public activée en est le banc : halo, perle et titres en Fredoka
   y sont réunis. L'utilisateur juge en jeu. Ce qui fait bon marché est
   simplifié ou abandonné ici, avant tout le reste.
2. **Composants** : `Btn` (tons `Action` et `Selected`), `Text.Label`, `Toggle`,
   `EffectsPicker`, `Fold`, `EmptyState`. Chacun arrive avec le premier écran
   qui s'en sert, pour ne jamais livrer un composant que rien n'emploie.
3. Le cadre.
4. Autour de vous.
5. Pairs.
6. Groupes et Public.
7. Demandes.
8. Réglages.
9. Fenêtres annexes : onboarding, entrée de groupe, notifications, badges,
   glyphes.

## Ce qui sera fidèle, et ce qui ne le sera pas

- **Fidèle** : palette, polices, pilules, interrupteurs, logo.
- **Approché** : le halo du fond et celui des éléments actifs. Sans flou ni
  dégradé radial, ce sont des formes empilées en transparence. Des marches
  peuvent se voir de près.
- **Incertain** : la perle à 10 px peut n'être qu'une bille bleue ; Nunito en
  petite taille peut baver là où Inter restait net. C'est ce que l'étape 1 tranche.

## Vérification

- À chaque étape : `dotnet build Linkpearl/Linkpearl.csproj -c Release` sans
  warning, les tests du noyau (inchangé, `ArchitectureTests` le garantit), puis
  `deploy-plugin-dev.sh`.
- En jeu, l'utilisateur juge : aucun test ne dit si c'est joli. Chaque étape dit
  quoi regarder, y compris à une échelle Dalamud au-dessus de 100 %.
- **`UiConventionTests`**, dans les tests du noyau qui tournent sous Linux : les
  sources de `Linkpearl/Ui` y sont embarquées comme celles du noyau le sont pour
  `ArchitectureTests`, et le test refuse `ImGui.Checkbox(`,
  `ImGui.CollapsingHeader(`, `ImGui.SameLine()` sans espacement, `ImGui.Button(`
  hors de `Components/`, `BtnTone.Primary`, et `Hex(0x` hors de `Theme.cs`.
  Chaque règle tient la liste des fichiers pas encore repris ; chaque étape en
  retire les siens, et la liste finit vide. Le test ne dit pas si c'est joli,
  seulement que plus rien ne contourne les composants.

## Hors périmètre

- La structure du shell et la navigation.
- Un thème clair : le site n'en a pas, le plugin non plus.
- Les libellés : l'interface reste en français, et le README anglais cite les
  libellés tels qu'ils sont. Si un libellé change au passage, les deux README
  suivent.
