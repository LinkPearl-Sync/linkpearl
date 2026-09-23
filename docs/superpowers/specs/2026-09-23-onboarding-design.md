# Présentation au premier usage

Écrit le 23 septembre 2026.

## Le besoin

Un joueur qui installe Linkpearl ne sait pas ce qu'il fait ni comment s'en
servir. Les clients comparables se découvrent en parcourant leurs pages ; ici
le geste central, se pairer, se fait en jeu, au clic droit sur un personnage,
et rien dans la fenêtre ne le dit d'emblée.

Une présentation courte et illustrée, montrée une fois, avec la bannière du
projet.

## Ce qui a été décidé avec l'utilisateur

- **Une fenêtre à étapes**, séparée de la fenêtre principale : bannière,
  titre, deux ou trois lignes, une illustration, points de progression,
  Précédent et Suivant.
- **Montrée au premier chargement du plugin**, une fois par installation, et
  non par personnage : ce qu'elle explique ne dépend pas de l'identité.
- **Illustrations dessinées en ImGui**, pas d'images : elles restent nettes à
  toute échelle, prennent les couleurs du thème et les vrais glyphes, et ne
  vieillissent pas quand l'interface change de teinte.
- **Quatre sujets** : le principe et la vie privée, le pairage en jeu, les
  contrôles, les prérequis et la sauvegarde.

Tranché sans objection :

- Une installation existante la voit une fois après la mise à jour.
- Pas de bouton « passer » : la croix ferme, et fermer compte comme vu.

## Les cinq écrans

1. **Bienvenue.** La bannière en grand. « Votre apparence moddée, visible par
   vos amis, de joueur à joueur. »
2. **Comment ça marche.** Deux silhouettes reliées par un lien marqué d'un
   cadenas, et le rendez-vous à l'écart, en pointillé, relié aux deux par un
   trait fin. Les fichiers passent directement, chiffrés ; le rendez-vous aide
   à se trouver et ne stocke rien ; rien ne s'échange sans l'accord des deux.
3. **Se pairer en jeu.** Une fausse plaque de nom avec le glyphe « disponible »,
   à côté le menu clic droit avec « Linkpearl : demander le pairage », puis la
   carte de demande telle que l'autre la reçoit. Dessous, la légende des cinq
   couleurs de glyphe, la même que dans Réglages.
4. **Garder la main.** Maquette d'une ligne de pair avec pause, et des bascules
   animations, VFX et sons. Le texte dit que ces blocages existent en global et
   par pair, et que « réappliquer » est au clic droit.
5. **Avant de commencer.** Penumbra et Glamourer avec leur état réel, coche
   verte ou croix rouge. Un rappel : après le premier pairage, sauvegarder son
   identité depuis Réglages, sans quoi une réinstallation la perd. Le bouton
   « C'est parti » remplace Suivant.

La bannière occupe toute la largeur sur l'écran 1, et un bandeau réduit
(hauteur fixe, rapport conservé, centré) en haut des écrans 2 à 5.

## Comportement

- `Configuration.OnboardingSeen`, faux par défaut. Il passe à vrai et
  s'enregistre dès que la fenêtre se ferme, quelle qu'en soit la manière
  (`OnClose`), pour ne jamais redemander.
- Au chargement, si le drapeau est faux, la fenêtre s'ouvre sur l'écran 1.
- « C'est parti » ferme la présentation et ouvre la fenêtre principale sur
  « Autour ».
- Un bouton « Revoir la présentation » dans Réglages la rouvre sur l'écran 1.
- Rouverte, elle repart toujours de l'écran 1.

## Découpage

- `Linkpearl/Assets/Images/banner.png` : la bannière fournie (WebP 1774×887,
  fond transparent), convertie en PNG et réduite à 1100 px de large, soit
  deux fois la largeur logique de la fenêtre pour rester nette à 200 %.
  Embarquée par `EmbeddedResource`, `LogicalName="Images.banner.png"`, comme
  les polices.
- `Ui/Onboarding/OnboardingWindow.cs` : un `ThemedWindow` qui tient l'écran
  courant, dessine la bannière, le titre, le texte, délègue l'illustration,
  puis la navigation. Taille logique fixe autour de 560×520, non
  redimensionnable, centrée à l'apparition.
- `Ui/Onboarding/OnboardingArt.cs` : une méthode statique par illustration,
  qui réserve sa zone avec `ImGui.Dummy` et dessine dans la `ImDrawList` de la
  fenêtre. Couleurs du `Theme`, glyphes de `NameplateGlyphs.ColorOf`,
  tailles passées par `Theme.S`.
- La bannière vient d'`ITextureProvider.GetFromManifestResource`, lue par
  `GetWrapOrEmpty()` à chaque image : le fournisseur partagé de Dalamud gère
  le chargement et la libération, rien à disposer de notre côté. Tant que la
  texture n'est pas prête, la zone reste vide à sa taille, pour que la mise en
  page ne saute pas.
- `Configuration.cs` : le drapeau.
- `SettingsPage.cs` : le bouton, via une `Action` passée par `MainWindow`.
- `Plugin.cs` : `[PluginService] ITextureProvider`, construction de la
  fenêtre, ouverture au premier chargement.

## Les prérequis en direct

L'état de Penumbra et Glamourer se lit par IPC. Le dessin ne doit rien
interroger (règle de `MainWindow`) : `Plugin` rafraîchit deux booléens toutes
les deux secondes depuis `Framework.Update`, seulement tant que la
présentation est ouverte, et la fenêtre les lit par un `Func<bool>` chacun.

## Ce qui n'est pas fait

- Aucune traduction : le plugin est en français partout.
- Aucune animation des illustrations.
- Aucun changement au noyau ; rien ici ne se teste sous Linux.

## Vérification

- `dotnet build Linkpearl/Linkpearl.csproj -c Release` sans warning.
- Les tests du noyau restent verts.
- En jeu via `deploy-plugin-dev.sh` : apparition au premier chargement (drapeau
  remis à faux à la main), les cinq écrans à 100 % et 150 %, la bannière nette,
  les prérequis justes Penumbra désactivé puis activé, « C'est parti » ouvre
  « Autour », la croix empêche une réapparition, le bouton de Réglages rouvre.
