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
- **Le choix du cache** : un dossier et un quota en gigaoctets, de 10 à 500,
  50 par défaut. Une apparence pèse environ 800 Mo : 50 Go en gardent une
  soixantaine.
- **La synchronisation attend la fin de la présentation** la première fois,
  pour que le cache naisse dans le dossier choisi et pas ailleurs.
- **Réglages reprend les deux** : le quota à chaud, le dossier au prochain
  chargement.

Tranché sans objection :

- Une installation existante la voit une fois après la mise à jour.
- Pas de bouton « passer » : la croix ferme, et fermer compte comme vu.

## Les six écrans

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
5. **Votre cache.** Les apparences reçues sont gardées sur le disque pour ne
   pas les retélécharger. Le dossier, avec un bouton « Parcourir », et le
   quota, un curseur en gigaoctets. Dessous, l'espace libre du disque choisi,
   et un avertissement si le quota le dépasse. Illustration : une jauge du
   quota, la part d'une apparence moyenne marquée dessus.
6. **Avant de commencer.** Penumbra et Glamourer avec leur état réel, coche
   verte ou croix rouge. Un rappel : après le premier pairage, sauvegarder son
   identité depuis Réglages, sans quoi une réinstallation la perd. Le bouton
   « C'est parti » remplace Suivant.

La bannière occupe toute la largeur sur l'écran 1, et un bandeau réduit
(hauteur fixe, rapport conservé, centré) en haut des écrans 2 à 6.

## Le cache

### Le dossier

- **Linkpearl ne remplit jamais directement le dossier choisi** : il y crée
  `LinkpearlCache` et n'écrit que là. Choisir `D:\Jeux` ne mélange rien aux
  fichiers qui s'y trouvent, et supprimer le cache ne touche qu'à ce qui est à
  nous. Le défaut reste `%LocalAppData%\Linkpearl\cache`, que rien ne change.
- Le sélecteur est le `FileDialogManager` de Dalamud (`OpenFolderDialog`),
  dessiné par la fenêtre qui l'a ouvert.
- Un dossier n'est retenu qu'après vérification : chemin absolu, sous-dossier
  créé, un fichier témoin écrit puis effacé. Sinon, un message sous le champ,
  et la configuration garde l'ancienne valeur.
- `Configuration.CacheDirectory` ne change pas de sens (vide pour le défaut) ;
  il reçoit désormais le chemin de `LinkpearlCache`, pas celui choisi.

### Le quota

- Un curseur de 10 à 500 Go, pas de 5. Le défaut de `CacheQuotaBytes` passe de
  20 à 50 Go ; une configuration qui a déjà enregistré 20 les garde.
- L'espace libre se lit sur le disque du dossier choisi, au choix du dossier
  et non à chaque image.

### La barrière de la première fois

Tant que `OnboardingSeen` est faux, rien ne lit ni n'écrit le cache : le
magasin de blobs n'est pas construit, notre apparence n'est pas capturée, le
moteur ne démarre pas à la connexion. À la fermeture de la présentation, par
« C'est parti » ou par la croix, le magasin est construit avec le dossier et
le quota affichés, puis tout démarre comme aujourd'hui.

`LocalAppearance` et `RemoteApplicator` reçoivent un magasin commutable
(`SwitchableBlobStore`, dans le noyau) : un `IBlobStore` qui délègue au
`FileSystemBlobStore` qu'on lui attache, et refuse tout tant qu'il n'en a
aucun. Elles restent construites au chargement ; seul le magasin réel arrive
après la barrière, et repart si le dossier disparaît. Le moteur, lui, n'est
construit qu'une fois le magasin attaché. Toute la décision (attendre, ouvrir,
bloquer, rechoisir) vit dans `Core/Cache/CacheKeeper`, testé sous Linux ;
`Configuration` lui fournit ses réglages par une interface du noyau.

Une installation déjà configurée, qui voit la présentation après la mise à
jour, attend de la même façon : son dossier actuel est proposé, et le garder
ne change rien.

### Dans Réglages

- **Quota** : le même curseur, appliqué tout de suite. Le magasin expose un
  moyen de changer son quota ; s'il est dépassé, l'éviction suivante le
  ramène sous le seuil.
- **Dossier** : le même champ. Le nouveau chemin s'enregistre et prend effet
  au prochain chargement, ce qu'un message dit sous le champ. Changer à chaud
  casserait les mods temporaires de Penumbra, qui pointent sur les fichiers du
  cache en cours.
- **L'ancien cache** : au chargement qui suit un changement, l'ancien chemin
  est gardé dans `Configuration.PreviousCacheDirectory`, et Réglages affiche
  sa taille avec « Supprimer l'ancien cache ». La suppression tourne sur le
  pool de threads et n'efface que des fichiers dont le nom est un hash de blob,
  plus l'index ; le dossier n'est retiré que s'il est vide ensuite. Un fichier
  étranger y survit toujours.
- La présentation rouverte depuis Réglages se comporte comme Réglages : le
  dossier prend effet au prochain chargement.

### Le dossier disparu

Condition posée par l'utilisateur, et non négociable : **si le dossier du
cache n'existe plus, le plugin s'arrête et demande d'en choisir un autre.** Il
ne le recrée jamais de lui-même. Un `Directory.CreateDirectory` discret
remplirait de nouveau un disque que l'utilisateur vient peut-être de
débrancher ou de vider exprès, ou pire un chemin qui n'est plus le sien.

- **Établi ou non.** `Configuration.CacheEstablished` passe à vrai la première
  fois que le magasin crée son dossier. Seul un cache établi peut disparaître :
  le tout premier démarrage, lui, crée le dossier normalement. Le défaut sous
  `%LocalAppData%` suit la même règle qu'un dossier choisi.
- **Au chargement.** Si le cache est établi et que son dossier manque, le
  magasin n'est pas construit, et la barrière reste fermée comme avant la
  présentation.
- **En cours de jeu.** La boucle de synchronisation vérifie l'existence du
  dossier toutes les cinq secondes, et toute écriture qui échoue sur un
  `DirectoryNotFoundException` le signale aussitôt. Dès la détection, le
  moteur s'arrête, chaque pair affiché est rendu à son apparence par défaut
  (ses mods temporaires pointent sur des fichiers qui n'existent plus), la
  capture de notre apparence s'arrête, et le magasin est abandonné.
- **Ce que voit l'utilisateur.** La fenêtre principale s'ouvre sur une page
  « Dossier du cache introuvable » qui remplace toutes les autres : le chemin
  disparu, `CacheChooser`, et rien d'autre à faire. La barre de statut affiche
  « cache introuvable ». Une notification Dalamud le dit une fois,
  pour le joueur qui a la fenêtre fermée.
- **La reprise.** Un dossier valide choisi, le magasin est reconstruit dedans
  et tout redémarre, sans rechargement : rien ne pointe plus sur l'ancien,
  puisque tout a été retiré à l'arrêt. Rechoisir le même chemin est permis,
  c'est un choix explicite.

Ce mécanisme demande que le magasin, `LocalAppearance`, `RemoteApplicator` et
le moteur se démontent et se reconstruisent ensemble : c'est le même chemin
que la barrière de la première fois, emprunté une fois de plus.

### L'éviction, qui n'existe pas encore

Rien n'appelle `EvictToAsync` aujourd'hui : le quota refuse un blob plus gros
que lui, mais le cache grossit sans fin. Un quota qu'on demande à
l'utilisateur doit être tenu.

Le moteur vérifie `NeedsEviction` à la fin de son tic, au plus toutes les
trente secondes : sommer la taille de dizaines de milliers de blobs à chaque
seconde serait du calcul pour rien. L'éviction descend
à `EvictionTarget` et épargne les blobs des apparences actuellement posées sur
des pairs et ceux de notre propre apparence : les retirer casserait ce qui est
à l'écran. La collecte de cet ensemble épinglé se fait dans le noyau, testée
sous Linux.

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
- `Ui/CacheChooser.cs` : le champ de dossier, le sélecteur, le curseur de
  quota et l'espace libre, partagé par la présentation et Réglages.
- `Configuration.cs` : `OnboardingSeen`, `CacheEstablished`, `PreviousCacheDirectory`, le défaut
  du quota.
- `SettingsPage.cs` : le bouton « Revoir la présentation », une section
  Cache avec `CacheChooser` et l'ancien cache.
- `Core/Cache` : changement de quota à chaud sur `FileSystemBlobStore`, et la
  suppression prudente d'un ancien cache.
- `Core/Sync` : l'ensemble des blobs épinglés, et le déclenchement de
  l'éviction.
- `Plugin.cs` : `[PluginService] ITextureProvider`, construction de la
  fenêtre, ouverture au premier chargement, barrière avant le cache.

## Les prérequis en direct

L'état de Penumbra et Glamourer se lit par IPC. Le dessin ne doit rien
interroger (règle de `MainWindow`) : `Plugin` rafraîchit deux booléens toutes
les deux secondes depuis `Framework.Update`, seulement tant que la
présentation est ouverte, et la fenêtre les lit par un `Func<bool>` chacun.

## Ce qui n'est pas fait

- Aucune traduction : le plugin est en français partout.
- Aucune animation des illustrations.
- Aucun déplacement du cache d'un dossier à l'autre : il se régénère.

## Vérification

- Sous Linux : le quota changé à chaud, la détection d'un dossier disparu, l'éviction qui épargne l'épinglé, la
  suppression d'un ancien cache qui laisse un fichier étranger et le dossier
  qui le contient.
- `dotnet build Linkpearl/Linkpearl.csproj -c Release` sans warning.
- En jeu via `deploy-plugin-dev.sh` : apparition au premier chargement (drapeau
  remis à faux à la main), les six écrans à 100 % et 150 %, la bannière nette,
  les prérequis justes Penumbra désactivé puis activé, aucun pair synchronisé
  avant la fermeture, le cache créé dans `LinkpearlCache` du dossier choisi,
  « C'est parti » ouvre « Autour », la croix empêche une réapparition, le
  bouton de Réglages rouvre, un quota abaissé sous la taille actuelle fait
  maigrir le cache, un changement de dossier dans Réglages attend le
  chargement suivant puis propose l'ancien à la suppression, et le dossier du
  cache supprimé en cours de jeu arrête tout, rend les pairs à leur apparence
  par défaut, puis repart dans le dossier rechoisi ; supprimé plugin éteint, il
  bloque dès le chargement.
