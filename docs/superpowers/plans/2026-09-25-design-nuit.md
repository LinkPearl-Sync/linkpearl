# Refonte visuelle « la nuit du site » : plan d'implémentation

> **Pour les agents :** sous-skill requis, superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans, pour exécuter ce plan tâche par tâche. Les étapes se cochent (`- [ ]`).

**Objectif :** donner au plugin l'apparence de son site et de son logo (nuit marine, halo bleu, orange pompon, perle, Fredoka et Nunito), et faire passer chaque écran par les mêmes composants.

**Architecture :** tout part des jetons de `Theme`, des polices de `Fonts` et de trois primitives de `Surface` (fond nuit, halo, perle), que les 27 fichiers de l'interface consomment déjà. Au-dessus, des composants (`Btn`, `Toggle`, `EffectsPicker`, `Fold`, `Text.Label`, `Brand`), puis les écrans un par un. Un test lit les sources de l'interface sous Linux et refuse tout ce qui contourne les composants ; sa liste de fichiers en attente rétrécit à chaque tâche.

**Technologies :** C# (.NET 10), Dalamud.NET.Sdk 15, `Dalamud.Bindings.ImGui`, xunit 2.9 pour le noyau, fonttools 4.66.0 (Python) pour générer les polices.

**Spec :** `docs/superpowers/specs/2026-09-25-design-nuit-design.md`

## Contraintes globales

- Toute dimension en pixels passe par `Theme.S()`. Aucune exception.
- Aucune couleur en dur hors de `Theme.cs` : tout passe par un jeton.
- `Linkpearl/Core/` ne référence jamais Dalamud (`ArchitectureTests`).
- `dotnet build Linkpearl/Linkpearl.csproj -c Release` passe **sans warning** à chaque commit.
- Commentaires en français, qui disent le *pourquoi*. Jamais de tiret cadratin (U+2014), ni dans le code, ni dans les commentaires, ni dans les commits.
- Commits en Conventional Commits, sujet en français. **Jamais** de ligne `Co-Authored-By:` ni `Claude-Session:`, ni d'URL de session, ni de mention « Generated with ».
- Chaque tâche se termine par `./scripts/deploy-plugin-dev.sh`, lancé sans demander, puis par ce que l'utilisateur doit regarder en jeu.
- L'interface ne se teste pas sous Linux : `UiConventionTests` garde les conventions, l'utilisateur juge le rendu.
- Les libellés ne changent pas. S'il le fallait, `README.md` et `README.fr.md` suivraient ensemble.
- Les numéros de ligne cités sont ceux du commit `db9d34a` ; se fier au texte cité plutôt qu'au numéro si l'arbre a bougé.

## Points de vigilance

Ce que la spec implique sans qu'aucun test ne l'exerce, du plus probable au moins probable. L'interface ne se teste pas sous Linux : chacun a sa vérification en jeu, dans la tâche qui le porte.

1. **Échelle Dalamud au-dessus de 100 %** : pilules, interrupteurs, halos et logo doivent grandir ensemble. Tout passe par `Theme.S()` ; vérification à 150 % dans les tâches 3, 5 et 10.
2. **Fenêtre au plus étroit (520 px logiques)** : un libellé long d'un `Toggle` ne doit pas passer sous l'interrupteur. `Toggle.Draw` rogne le libellé avant la piste ; vérification en réduisant la fenêtre, tâche 10.
3. **Notifications posées sur un décor clair** (neige de Coerthas, sable de Thanalan) : leurs cartes restent opaques, `background: Theme.BgSurface` ; tâche 9.
4. **Police ou texture absente** : un atlas qui ne se construit pas laisse `Fonts` sur la police de Dalamud, et un logo pas encore chargé ne dessine rien. `Brand.Draw` rend `false` sans rien lever, et chaque appelant retombe sur l'icône ou garde sa place ; tâche 5.
5. **Un nom de groupe en latin étendu** (« Łódź ») dans un titre en Fredoka : Inter, fusionné derrière, dessine ce que Fredoka n'a pas ; tâche 2, vérifié en tâche 8.

---

### Tâche 1 : le garde-fou des conventions de l'interface

**Fichiers :**
- Créer : `Linkpearl.Core.Tests/UiConventionTests.cs`
- Modifier : `Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` (embarquer les sources de l'interface)
- Modifier : `Linkpearl.Core.Tests/ArchitectureTests.cs` (ne lire que les sources du noyau)

**Interfaces :**
- Produit : `UiConventionTests.Rules`, une `TheoryData<string, string, string[], string[]>` (motif interdit, raison, exemptés pour de bon, **en attente**). Chaque tâche suivante retire ses fichiers de la dernière colonne. Les noms de fichier sont relatifs à `Linkpearl/Ui`, séparés par des points : `Pages.GroupsPage.cs`, `Onboarding.OnboardingArt.cs`.

- [ ] **Étape 1 : embarquer les sources de l'interface**

Dans `Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`, juste avant `<Import Project="..\build\Core.Deps.props" />` :

```xml
  <!-- Les sources de l'interface, embarquées pour UiConventionTests. Jamais
       compilées ici : elles dépendent de Dalamud. -->
  <ItemGroup>
    <EmbeddedResource Include="..\Linkpearl\Ui\**\*.cs" LinkBase="UiSources" />
  </ItemGroup>
```

- [ ] **Étape 2 : constater que le garde-fou du noyau lit maintenant l'interface**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ArchitectureTests`
Attendu : FAIL sur `Le_noyau_n_importe_rien_du_jeu_ni_de_l_interface`, « using Dalamud » trouvé dans des ressources `UiSources` : `CoreSources()` lit toutes les ressources `.cs`.

- [ ] **Étape 3 : restreindre `CoreSources()` au noyau**

Dans `ArchitectureTests.cs`, remplacer :

```csharp
            .Where(name => name.EndsWith(".cs", StringComparison.Ordinal))
```

par :

```csharp
            // Le préfixe, pas seulement l'extension : les sources de
            // l'interface sont embarquées aussi, pour UiConventionTests, et
            // elles ont le droit d'importer Dalamud.
            .Where(name => name.Contains(".CoreSources.", StringComparison.Ordinal)
                        && name.EndsWith(".cs", StringComparison.Ordinal))
```

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ArchitectureTests`
Attendu : PASS.

- [ ] **Étape 4 : écrire `UiConventionTests`**

`Linkpearl.Core.Tests/UiConventionTests.cs` :

```csharp
using System.Reflection;
using Xunit;

namespace Linkpearl.Core.Tests;

/// <summary>
/// Fait respecter les conventions de l'interface : rien ne contourne les composants.
/// </summary>
/// <remarks>
/// L'interface ne se teste pas sous Linux, elle dépend de Dalamud. Ses sources,
/// si : elles sont embarquées comme celles du noyau. Chaque règle tient la liste
/// des fichiers que la refonte n'a pas encore repris ; une liste qui ne rétrécit
/// plus est une refonte qui s'est arrêtée en route.
///
/// Le test ne dit pas si c'est joli. Il dit seulement qu'une case ImGui brute,
/// une couleur en dur ou un espacement laissé au défaut ne reviendront pas en
/// douce une fois l'écran repris.
/// </remarks>
public class UiConventionTests
{
    private const string Prefix = ".UiSources.";

    private static IReadOnlyList<(string File, string Source)> UiSources()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(Prefix, StringComparison.Ordinal)
                        && name.EndsWith(".cs", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var file = name[(name.IndexOf(Prefix, StringComparison.Ordinal) + Prefix.Length)..];
                return (file, reader.ReadToEnd());
            })
            .ToList();
    }

    /// <summary>Motif interdit, raison, fichiers exemptés pour de bon, fichiers pas encore repris.</summary>
    public static TheoryData<string, string, string[], string[]> Rules => new()
    {
        {
            "ImGui.Checkbox(", "un réglage oui/non passe par Toggle", [],
            ["Pages.GroupsPage.cs", "Pages.PairsPage.cs", "Pages.SettingsPage.cs"]
        },
        {
            "ImGui.CollapsingHeader(", "un en-tête repliable passe par Fold", [],
            ["Pages.NearbyPage.cs", "Pages.PairsPage.cs"]
        },
        {
            "ImGui.SameLine()", "l'espacement se dit, il ne se laisse pas au défaut d'ImGui", [],
            ["NameplateLegend.cs", "Pages.RequestsPage.cs", "Pages.SettingsPage.cs", "RequestToasts.cs"]
        },
        {
            "ImGui.Button(", "un bouton passe par Btn", ["Components.Btn.cs"], []
        },
        {
            "Hex(0x", "une couleur vient d'un jeton de Theme", ["Theme.cs"],
            ["NameplateGlyphs.cs", "Onboarding.OnboardingArt.cs"]
        },
    };

    [Fact]
    public void Le_test_inspecte_reellement_l_interface()
    {
        // Garde-fou du garde-fou : un préfixe de ressource mal écrit ferait
        // passer toutes les règles au vert en n'inspectant rien.
        var files = UiSources().Select(source => source.File).ToList();

        Assert.Contains("Theme.cs", files);
        Assert.Contains("Pages.GroupsPage.cs", files);
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rien_ne_contourne_les_composants(string forbidden, string why, string[] exempt, string[] pending)
    {
        var offenders = UiSources()
            .Where(source => source.Source.Contains(forbidden, StringComparison.Ordinal))
            .Select(source => source.File)
            .Where(file => exempt.Contains(file) is false)
            .ToHashSet();

        var unexpected = offenders.Except(pending).Order().ToList();

        Assert.True(unexpected.Count == 0,
            $"« {forbidden} » interdit ({why}) dans : {string.Join(", ", unexpected)}");

        // Un fichier repris doit quitter la liste : il pourrait sinon régresser
        // sans que rien ne tombe.
        var stale = pending.Except(offenders).Order().ToList();

        Assert.True(stale.Count == 0,
            $"Déjà repris, à retirer des fichiers en attente de « {forbidden} » : {string.Join(", ", stale)}");
    }
}
```

- [ ] **Étape 5 : lancer le test**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : PASS. Si une règle signale un fichier « inattendu » ou « déjà repris », l'arbre a bougé depuis l'écriture du plan : corriger la liste en attente d'après le message, jamais le motif.

- [ ] **Étape 6 : toute la suite**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : PASS, tous les tests.

- [ ] **Étape 7 : commit**

```bash
git add Linkpearl.Core.Tests/UiConventionTests.cs Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj Linkpearl.Core.Tests/ArchitectureTests.cs
git commit -m "test(ui): garde-fou des conventions de l'interface"
```

---

### Tâche 2 : Fredoka et Nunito, Inter en secours

**Fichiers :**
- Créer : `scripts/generer-polices.sh`
- Créer (générés) : `Linkpearl/Assets/Fonts/Fredoka-SemiBold.ttf`, `Linkpearl/Assets/Fonts/Nunito-Regular.ttf`, `Linkpearl/Assets/Fonts/Fredoka-OFL.txt`, `Linkpearl/Assets/Fonts/Nunito-OFL.txt`
- Modifier : `Linkpearl/Linkpearl.csproj` (ressources de police)
- Modifier : `Linkpearl/Ui/Fonts.cs` (`Build` et `Compose`)

**Interfaces :**
- Produit : les ressources `Fonts.Fredoka-SemiBold.ttf` et `Fonts.Nunito-Regular.ttf`. `Fonts.PushTitle/PushH2/PushBody/PushSmall` ne changent pas de signature.

- [ ] **Étape 1 : écrire le script de génération**

`scripts/generer-polices.sh` :

```bash
#!/usr/bin/env bash
# Tire Fredoka SemiBold et Nunito Regular en statique depuis les fichiers
# variables de Google Fonts, puis vérifie que chaque caractère des chaînes de
# l'interface existe dans la police principale ou dans Inter, derrière.
#
# Statiques parce que l'atlas ImGui ne choisit pas la graisse d'une police
# variable. Le commit de google/fonts et la version de fonttools sont figés :
# c'est ce qui rend les fichiers embarqués reproductibles.
set -euo pipefail

root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/Linkpearl/Assets/Fonts"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

commit=23e54b51ddffbc7713c583748e3bd86f62b1fa4a
base="https://raw.githubusercontent.com/google/fonts/$commit/ofl"

python3 -m venv "$work/venv"
"$work/venv/bin/pip" install --quiet fonttools==4.66.0

curl -fsSL -o "$work/Nunito.ttf"  "$base/nunito/Nunito%5Bwght%5D.ttf"
curl -fsSL -o "$work/Fredoka.ttf" "$base/fredoka/Fredoka%5Bwdth,wght%5D.ttf"
curl -fsSL -o "$out/Nunito-OFL.txt"  "$base/nunito/OFL.txt"
curl -fsSL -o "$out/Fredoka-OFL.txt" "$base/fredoka/OFL.txt"

"$work/venv/bin/fonttools" varLib.instancer -q "$work/Nunito.ttf"  wght=400          -o "$out/Nunito-Regular.ttf"
"$work/venv/bin/fonttools" varLib.instancer -q "$work/Fredoka.ttf" wght=600 wdth=100 -o "$out/Fredoka-SemiBold.ttf"

"$work/venv/bin/python" - "$root" "$out" <<'EOF'
import glob, re, sys
from fontTools.ttLib import TTFont

root, out = sys.argv[1], sys.argv[2]

# Tout caractère non ASCII des littéraux du plugin, hors zone d'usage privé :
# FontAwesome et les symboles du jeu y vivent, et ont leur propre police.
chars = set()
for path in glob.glob(f"{root}/Linkpearl/**/*.cs", recursive=True):
    if "/obj/" in path or "/bin/" in path:
        continue
    for literal in re.findall(r'"(?:[^"\\]|\\.)*"', open(path, encoding="utf-8").read()):
        chars |= {c for c in literal if ord(c) > 127 and not 0xE000 <= ord(c) <= 0xF8FF}

failed = False
for main, backup in {"Nunito-Regular.ttf": "Inter-Regular.ttf",
                     "Fredoka-SemiBold.ttf": "Inter-SemiBold.ttf"}.items():
    primary = TTFont(f"{out}/{main}").getBestCmap()
    fallback = TTFont(f"{out}/{backup}").getBestCmap()
    borrowed = sorted(c for c in chars if ord(c) not in primary and ord(c) in fallback)
    missing = sorted(c for c in chars if ord(c) not in primary and ord(c) not in fallback)
    print(f"{main} : {len(borrowed)} glyphe(s) pris à {backup} : {''.join(borrowed)}")
    if missing:
        print(f"{main} : introuvable, même dans {backup} : {''.join(missing)}")
        failed = True

sys.exit(1 if failed else 0)
EOF
```

Puis : `chmod +x scripts/generer-polices.sh`

- [ ] **Étape 2 : générer les polices**

Run : `./scripts/generer-polices.sh`
Attendu : code de sortie 0, et deux lignes comme :

```
Nunito-Regular.ttf : 3 glyphe(s) pris à Inter-Regular.ttf : →◆◇
Fredoka-SemiBold.ttf : 4 glyphe(s) pris à Inter-SemiBold.ttf : →≈◆◇
```

Si une ligne « introuvable » apparaît, s'arrêter : un caractère de l'interface ne serait dessiné par aucune police.

- [ ] **Étape 3 : déclarer les ressources**

Dans `Linkpearl/Linkpearl.csproj`, remplacer le commentaire `<!-- Inter, sous-ensemblé, embarqué. ... -->` et l'`<ItemGroup>` qui le suit par :

```xml
  <!-- Fredoka pour les titres et Nunito pour le texte, les polices du site,
       tirées en statique par scripts/generer-polices.sh. Inter, sous-ensemblé,
       reste fusionné derrière pour les glyphes qu'elles n'ont pas. Toutes sous
       licence SIL OFL, dont les textes accompagnent les fichiers. Les noms
       logiques sont ceux que Fonts.cs demande à l'assembly. -->
  <ItemGroup>
    <EmbeddedResource Include="Assets\Fonts\Fredoka-SemiBold.ttf" LogicalName="Fonts.Fredoka-SemiBold.ttf" />
    <EmbeddedResource Include="Assets\Fonts\Nunito-Regular.ttf"   LogicalName="Fonts.Nunito-Regular.ttf" />
    <EmbeddedResource Include="Assets\Fonts\Inter-Regular.ttf"    LogicalName="Fonts.Inter-Regular.ttf" />
    <EmbeddedResource Include="Assets\Fonts\Inter-SemiBold.ttf"   LogicalName="Fonts.Inter-SemiBold.ttf" />
  </ItemGroup>
```

- [ ] **Étape 4 : composer chaque poignée avec sa police de secours**

Dans `Fonts.cs`, le résumé de classe devient :

```csharp
/// <summary>
/// Polices du plugin : Fredoka pour les titres, Nunito pour le texte, les
/// polices du site. Inter est fusionné derrière chacune, et FontAwesome dans le
/// corps de texte pour que les icônes s'écrivent au fil du texte, sans bascule.
/// </summary>
```

Le commentaire au-dessus de `_textRanges = new FluentGlyphRangeBuilder()` devient :

```csharp
            // Ces plages doivent couvrir tout ce que le fichier Inter contient :
            // il sert de secours derrière Fredoka et Nunito, et un glyphe
            // absent d'ici n'est pas chargé dans l'atlas, il s'affiche en
            // caractère de remplacement.
```

Les quatre poignées deviennent :

```csharp
            Body  = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Nunito-Regular.ttf", "Fonts.Inter-Regular.ttf", 15f)));
            Small = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Nunito-Regular.ttf", "Fonts.Inter-Regular.ttf", 12f)));
            H2    = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Fredoka-SemiBold.ttf", "Fonts.Inter-SemiBold.ttf", 17f)));
            Title = _atlas.NewDelegateFontHandle(tk => tk.OnPreBuild(
                p => Compose(p, "Fonts.Fredoka-SemiBold.ttf", "Fonts.Inter-SemiBold.ttf", 22f)));
```

Le début de `Compose`, jusqu'à la ligne `// Accents et alphabets supplémentaires selon la langue réglée dans Dalamud.` exclue, devient :

```csharp
    private static void Compose(IFontAtlasBuildToolkitPreBuild p, string resource, string fallback, float sizePx)
    {
        var cfg = new SafeFontConfig { SizePx = sizePx, GlyphRanges = _textRanges };

        using var stream = Asm.GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"ressource de police introuvable : {resource}");

        var font = p.AddFontFromStream(stream, in cfg, leaveOpen: false, resource);

        // Inter derrière, sur les mêmes plages. ImGui garde le premier glyphe
        // venu : Fredoka et Nunito dessinent tout ce qu'elles ont, Inter ne
        // comble que leurs trous. Mesuré le 25 septembre : Nunito n'a ni →, ni
        // ◆, ni ◇, et Fredoka ne couvre que 10 caractères sur 128 du latin
        // étendu A, qu'un nom de groupe peut porter.
        using var fallbackStream = Asm.GetManifestResourceStream(fallback)
            ?? throw new FileNotFoundException($"ressource de police introuvable : {fallback}");

        var secours = new SafeFontConfig { SizePx = sizePx, GlyphRanges = _textRanges, MergeFont = font };
        p.AddFontFromStream(fallbackStream, in secours, leaveOpen: false, fallback);

```

Dans le commentaire des symboles du jeu, remplacer « ne chevauchent donc ni Inter ni FontAwesome » par « ne chevauchent donc ni les polices de texte ni FontAwesome ».

- [ ] **Étape 5 : compiler**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `0 Warning(s)`, `0 Error(s)`. Si les assemblies Dalamud manquent, lancer d'abord `./scripts/deploy-plugin-dev.sh`, qui les aligne.

- [ ] **Étape 6 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, `/xlplugins`, Dev Tools, recharger. Regarder :
- les titres de page et les noms en Fredoka (arrondis, un peu épais), le texte en Nunito ;
- une flèche `→` ou un losange `◆` quelque part dans l'interface, dessinés par Inter et non en `?` ;
- rien n'a basculé sur la police de Dalamud ; sinon, le journal dit « Une police du plugin n'a pas pu être construite ».

- [ ] **Étape 7 : commit**

```bash
git add scripts/generer-polices.sh Linkpearl/Assets/Fonts Linkpearl/Linkpearl.csproj Linkpearl/Ui/Fonts.cs
git commit -m "feat(ui): Fredoka et Nunito, les polices du site, Inter en secours"
```

---

### Tâche 3 : la palette, la nuit, le halo et la perle (essai en jeu)

**Fichiers :**
- Modifier : `Linkpearl/Ui/Theme.cs` (jetons)
- Modifier : `Linkpearl/Ui/Surface.cs` (primitives `Halo`, `NightBackground`, `Glow`, `Pearl`)
- Modifier : `Linkpearl/Ui/Shell/ThemedWindow.cs` (fond peint, `Draw` scellé)
- Modifier : `Linkpearl/Ui/MainWindow.cs:138`, `Linkpearl/Ui/RequestToasts.cs:101`, `Linkpearl/Ui/GroupEntryWindow.cs:108`, `Linkpearl/Ui/Onboarding/OnboardingWindow.cs:110` (`Draw` devient `DrawContents`)
- Modifier : `Linkpearl/Ui/Components/Card.cs` (fond translucide, halo à la place de la barre)
- Modifier : `Linkpearl/Ui/Shell/TitleBar.cs` (fond du bandeau)
- Modifier : `Linkpearl/Ui/Shell/StatusBar.cs` (perle de connexion)

**Interfaces :**
- Produit, dans `Theme` : `Action`, `ActionHover`, `ActionActive`, `TextOnAction`, `CardFill`, `CardFillHover`, `NightTop`, `NightHalo`, `PearlShine`, `PearlLight`, `PearlBody`, `PearlRim`, `PearlGlow` (tous `Vector4`).
- Produit, dans `Surface` :
  - `static void Halo(ImDrawListPtr dl, Vector2 center, Vector2 radius, Vector4 color, int segments = 48)`
  - `static void NightBackground(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, bool roundTop, float opacity)`
  - `static void Glow(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding, Vector4 color, float spread = 7f)`
  - `static void Pearl(ImDrawListPtr dl, Vector2 center, float radius, bool glow = true)`
- Produit, dans `ThemedWindow` : `protected abstract void DrawContents()`. `Draw()` devient `sealed`.

- [ ] **Étape 1 : la palette**

Dans `Theme.cs`, le commentaire de classe devient :

```csharp
/// <summary>
/// Jetons de design du plugin.
/// </summary>
/// <remarks>
/// La palette est celle du site et du logo : la nuit marine où se tiennent les
/// deux mogs, le halo bleu de la perle, et l'orange de leurs pompons, réservé à
/// l'action principale d'un écran. Chaque valeur cite la variable CSS du site
/// dont elle vient, pour que les deux ne dérivent pas.
///
/// Toute dimension en pixels passe par <see cref="S(float)"/>. Sans cela
/// l'interface devient illisible dès que l'utilisateur monte l'échelle Dalamud,
/// et c'est le genre de défaut qu'on ne voit jamais soi-même.
/// </remarks>
```

Remplacer les sections Fonds, Accents, Texte, Statuts et Bordures (de `// ─── Fonds` jusqu'à `BorderLight` inclus) par :

```csharp
    // ─── Fonds ────────────────────────────────────────────────────────────────
    //
    // Échelle de profondeur, du plus enfoncé au plus surélevé. La règle qui rend
    // une interface sombre lisible : chaque niveau doit être distinct du
    // précédent, sinon les cartes disparaissent dans le fond et tout paraît plat.
    //
    //   BgSunken  <  BgSidebar  <  BgBase  <  BgSurface  <  BgRaised  <  BgHover
    //
    // BgSurface, BgRaised et BgHover valent le blanc à 5, 9 et 14 % posé sur
    // --deep : ce sont les cartes du site, précalculées en opaque pour ce qui
    // doit masquer le décor (infobulles, notifications, menus).

    public static readonly Vector4 BgSunken  = Hex(0x040B26); // --field, champs de saisie
    public static readonly Vector4 BgBase    = Hex(0x07123A); // --deep, fond de fenêtre
    public static readonly Vector4 BgSidebar = Hex(0x060F33); // barres, un cran sous le fond
    public static readonly Vector4 BgSurface = Hex(0x131E44); // cartes opaques, infobulles
    public static readonly Vector4 BgRaised  = Hex(0x1D274C); // surface survolée
    public static readonly Vector4 BgHover   = Hex(0x2A3356); // survol d'un élément surélevé

    /// <summary>Fond d'une carte posée sur la nuit : translucide, pour laisser voir le halo.</summary>
    public static readonly Vector4 CardFill      = Hex(0xFFFFFF, 0.05f);
    public static readonly Vector4 CardFillHover = Hex(0xFFFFFF, 0.09f);

    /// <summary>Haut du dégradé de fond, et cœur du halo, tirés du fond du site.</summary>
    public static readonly Vector4 NightTop  = Hex(0x0C1E5C);
    public static readonly Vector4 NightHalo = Hex(0x1D3B9A);

    /// <summary>Ombre portée sous les surfaces surélevées.</summary>
    public static readonly Vector4 Shadow = Hex(0x000000, 0.50f);

    // ─── Accents ──────────────────────────────────────────────────────────────
    //
    // Le halo bleu (--glow) marque ce qui est actif ou choisi. L'orange pompon
    // (--pom) ne sert qu'à l'action principale d'un écran, comme le bouton
    // « Copier » du site : répandu, il ne désignerait plus rien.

    public static readonly Vector4 Accent       = Hex(0x5FB4FF);
    public static readonly Vector4 AccentHover  = Hex(0x8FCBFF);
    public static readonly Vector4 AccentActive = Hex(0x3D8FE0);

    /// <summary>Version assourdie, pour les fonds et les voiles.</summary>
    public static readonly Vector4 AccentMuted = Hex(0x173067);

    public static readonly Vector4 Action       = Hex(0xFFA45C);
    public static readonly Vector4 ActionHover  = Hex(0xFFB67A);
    public static readonly Vector4 ActionActive = Hex(0xE88A40);

    /// <summary>Texte posé sur l'orange, le brun du bouton « Copier » du site.</summary>
    public static readonly Vector4 TextOnAction = Hex(0x2A1300);

    // ─── Perle ────────────────────────────────────────────────────────────────
    //
    // Le dégradé des pastilles du site, du reflet au bord : blanc, bleu clair,
    // bleu, lavande. Le halo est celui de leur box-shadow.

    public static readonly Vector4 PearlShine = Hex(0xFFFFFF);
    public static readonly Vector4 PearlLight = Hex(0xBFE4FF);
    public static readonly Vector4 PearlBody  = Hex(0x7A9CFF);
    public static readonly Vector4 PearlRim   = Hex(0xC79BFF);
    public static readonly Vector4 PearlGlow  = Hex(0x7EC3FF);

    // ─── Texte ────────────────────────────────────────────────────────────────

    public static readonly Vector4 Text      = Hex(0xEAF1FF); // --ink
    public static readonly Vector4 TextMuted = Hex(0xA9B8E6); // --mute
    public static readonly Vector4 TextFaint = Hex(0x7F8DC0); // --faint
    public static readonly Vector4 Link      = Hex(0x8FD0FF); // les étoiles du site

    /// <summary>Texte posé sur une surface claire, l'accent par exemple.</summary>
    public static readonly Vector4 TextOnLight = Hex(0x0B1433);

    // ─── Statuts ──────────────────────────────────────────────────────────────

    /// <summary>Pair joint et apparence posée.</summary>
    public static readonly Vector4 Online = Hex(0x6FE0B0);

    /// <summary>Transfert en cours, ou pair en pause. Jaune et non orange : l'orange est à l'action.</summary>
    public static readonly Vector4 Idle = Hex(0xF5D06F);

    public static readonly Vector4 Danger      = Hex(0xFF6B7D);
    public static readonly Vector4 DangerHover = Hex(0xFF8A98);

    // ─── Bordures ─────────────────────────────────────────────────────────────
    //
    // --line du site, rgba(143,196,255,.22), posé sur --deep ; la douce à 12 %,
    // la claire à 35 %.

    public static readonly Vector4 Border      = Hex(0x253965);
    public static readonly Vector4 BorderSoft  = Hex(0x172752);
    public static readonly Vector4 BorderLight = Hex(0x37507F);
```

`Highlight` ne change pas. Les trois rayons deviennent :

```csharp
    public const float RadiusWindow = 12f;

    /// <summary>Le site en a 18 : ImGui adoucit mal un si grand rayon aux petites tailles.</summary>
    public const float RadiusCard   = 14f;

    public const float RadiusFrame  =  6f;
```

- [ ] **Étape 2 : les primitives de `Surface`**

Dans `Surface.cs`, le commentaire de classe devient :

```csharp
/// <summary>
/// Rendu des surfaces : ombre portée, fond, liseré, bordure, et la nuit du site,
/// son halo et sa perle.
/// </summary>
/// <remarks>
/// Sur fond sombre, une carte qui n'a qu'un fond légèrement différent reste
/// invisible. Ce qui la détache, c'est la combinaison de trois signaux : une
/// ombre diffuse en dessous, une bordure nette, et un liseré clair sur l'arête
/// haute qui simule une lumière zénithale.
///
/// ImGui n'a ni flou ni dégradé radial, et les bindings de Dalamud n'exposent
/// pas d'ellipse. Le halo est un éventail de triangles dont les sommets portent
/// la couleur : la carte graphique interpole d'un sommet à l'autre, ce qui donne
/// un dégradé continu, sans les marches qu'aurait une pile de cercles.
/// </remarks>
```

Ajouter à la fin de la classe :

```csharp
    /// <summary>Paliers du halo, du centre au bord : (fraction du rayon, fraction de l'alpha).</summary>
    /// <remarks>
    /// Un seul palier donnerait un cône, dont la pointe se voit au centre. Le
    /// palier du milieu tasse la lumière vers le cœur, comme un halo réel.
    /// </remarks>
    private static readonly (float Radius, float Alpha)[] HaloStops = [(0f, 1f), (0.45f, 0.45f), (1f, 0f)];

    /// <summary>Tache de lumière elliptique, pleine au centre, nulle au bord.</summary>
    public static void Halo(ImDrawListPtr dl, Vector2 center, Vector2 radius, Vector4 color, int segments = 48)
    {
        var uv    = ImGui.GetFontTexUvWhitePixel();
        var quads = segments * (HaloStops.Length - 1);

        // Six sommets par quadrilatère, sans partage : PrimVtx écrit l'index
        // avec le sommet, ce qui évite de tenir soi-même le compte des indices.
        dl.PrimReserve(quads * 6, quads * 6);

        for (var ring = 1; ring < HaloStops.Length; ring++)
        {
            var (innerRadius, innerAlpha) = HaloStops[ring - 1];
            var (outerRadius, outerAlpha) = HaloStops[ring];

            var inner = ImGui.GetColorU32(Theme.Alpha(color, color.W * innerAlpha));
            var outer = ImGui.GetColorU32(Theme.Alpha(color, color.W * outerAlpha));

            for (var i = 0; i < segments; i++)
            {
                var a = OnEllipse(center, radius * innerRadius, i, segments);
                var b = OnEllipse(center, radius * innerRadius, i + 1, segments);
                var c = OnEllipse(center, radius * outerRadius, i + 1, segments);
                var d = OnEllipse(center, radius * outerRadius, i, segments);

                dl.PrimVtx(a, uv, inner);
                dl.PrimVtx(d, uv, outer);
                dl.PrimVtx(c, uv, outer);

                dl.PrimVtx(a, uv, inner);
                dl.PrimVtx(c, uv, outer);
                dl.PrimVtx(b, uv, inner);
            }
        }
    }

    private static Vector2 OnEllipse(Vector2 center, Vector2 radius, int step, int segments)
    {
        var angle = MathF.Tau * step / segments;
        return center + new Vector2(MathF.Cos(angle) * radius.X, MathF.Sin(angle) * radius.Y);
    }

    /// <summary>
    /// Le fond du site : un dégradé vertical de la nuit, et le halo bleu du haut.
    /// </summary>
    /// <remarks>
    /// <c>AddRectFilledMultiColor</c> ne sait pas arrondir : le dégradé couvre
    /// le milieu, et deux bandes unies de la hauteur du rayon, aux couleurs de
    /// ses extrémités, portent les angles. La couture ne se voit pas, les
    /// couleurs se rejoignent exactement.
    /// </remarks>
    public static void NightBackground(ImDrawListPtr dl, Vector2 min, Vector2 max,
                                       float rounding, bool roundTop, float opacity)
    {
        var top    = ImGui.GetColorU32(Theme.Alpha(Theme.NightTop, opacity));
        var bottom = ImGui.GetColorU32(Theme.Alpha(Theme.BgBase, opacity));
        var r      = Math.Min(rounding, (max.Y - min.Y) * 0.5f);

        dl.AddRectFilled(min, new Vector2(max.X, min.Y + r + 1f), top, roundTop ? r : 0f,
                         roundTop ? ImDrawFlags.RoundCornersTop : ImDrawFlags.None);
        dl.AddRectFilledMultiColor(new Vector2(min.X, min.Y + r), new Vector2(max.X, max.Y - r),
                                   top, top, bottom, bottom);
        dl.AddRectFilled(new Vector2(min.X, max.Y - r - 1f), max, bottom, r, ImDrawFlags.RoundCornersBottom);

        // L'ellipse du site : 70 % de la largeur, 480 px de haut, centrée à
        // 160 px du bord. Sa largeur n'atteint pas les angles, et le rognage
        // l'arrête au bord haut.
        var width = max.X - min.X;

        dl.PushClipRect(min, max, true);
        Halo(dl, new Vector2(min.X + width * 0.5f, min.Y + Theme.S(160f)),
             new Vector2(width * 0.35f, Theme.S(240f)), Theme.Alpha(Theme.NightHalo, opacity));
        dl.PopClipRect();
    }

    /// <summary>
    /// Le halo d'un élément actif, autour de son rectangle arrondi.
    /// </summary>
    /// <remarks>
    /// Même technique que l'ombre : des contours de plus en plus larges et
    /// transparents. À peindre avant l'élément, qui recouvre l'intérieur.
    /// </remarks>
    public static void Glow(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding,
                            Vector4 color, float spread = 7f)
    {
        var steps = Math.Max(1, (int)MathF.Round(Theme.S(spread)));

        for (var i = 1; i <= steps; i++)
        {
            var t     = i / (float)steps;
            var alpha = color.W * 0.30f * (1f - t) * (1f - t);

            if (alpha <= 0.004f)
                continue;

            var grow = new Vector2(i, i);

            dl.AddRect(min - grow, max + grow, ImGui.GetColorU32(Theme.Alpha(color, alpha)),
                       rounding + i, ImDrawFlags.None, 1.5f);
        }
    }

    /// <summary>
    /// La pastille perle du site : bord lavande, corps bleu, reflet blanc en haut à gauche.
    /// </summary>
    /// <remarks>
    /// Des disques décalés vers la source de lumière plutôt qu'un dégradé
    /// radial : à quelques pixels, un dégradé se lit comme une bille unie,
    /// alors que le reflet net fait la perle.
    /// </remarks>
    public static void Pearl(ImDrawListPtr dl, Vector2 center, float radius, bool glow = true)
    {
        if (glow)
            Halo(dl, center, new Vector2(radius * 2.4f), Theme.Alpha(Theme.PearlGlow, 0.55f), 24);

        var light = new Vector2(-radius, -radius);

        dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.PearlRim));
        dl.AddCircleFilled(center + light * 0.10f, radius * 0.82f, ImGui.GetColorU32(Theme.PearlBody));
        dl.AddCircleFilled(center + light * 0.28f, radius * 0.50f, ImGui.GetColorU32(Theme.PearlLight));
        dl.AddCircleFilled(center + light * 0.36f, radius * 0.22f, ImGui.GetColorU32(Theme.PearlShine));
    }
```

- [ ] **Étape 3 : peindre la nuit sous chaque fenêtre**

Dans `ThemedWindow.cs`, remplacer la ligne `.Color(ImGuiCol.WindowBg, Theme.Alpha(Theme.BgBase, BackgroundOpacity))` et le commentaire « Seul le fond de fenêtre est translucide... » au-dessus par :

```csharp
            // Le fond natif s'efface : c'est la nuit, peinte au début de Draw,
            // qui porte l'opacité. Les cartes posées dessus sont translucides à
            // leur tour, comme sur le site, et laissent voir le halo.
            .Color(ImGuiCol.WindowBg,     Vector4.Zero)
```

Ajouter, après `OnCloseButton` :

```csharp
    /// <summary>La nuit sous le contenu, puis le contenu.</summary>
    /// <remarks>
    /// Scellée : une fenêtre qui oublierait d'appeler la peinture du fond
    /// resterait transparente sur le décor du jeu. Les fenêtres sans fond,
    /// comme les notifications, n'en reçoivent pas.
    /// </remarks>
    public sealed override void Draw()
    {
        if ((Flags & ImGuiWindowFlags.NoBackground) == 0)
            PaintNight();

        DrawContents();
    }

    /// <summary>Le contenu de la fenêtre, dessiné sur la nuit.</summary>
    protected abstract void DrawContents();

    private void PaintNight()
    {
        var position = ImGui.GetWindowPos();
        var size     = ImGui.GetWindowSize();
        var dl       = ImGui.GetWindowDrawList();

        // Sous la barre de titre native quand elle existe : elle est dessinée
        // avant le contenu dans la même liste, la recouvrir effacerait le titre.
        var titled = (Flags & ImGuiWindowFlags.NoTitleBar) == 0;
        var top    = titled ? ImGui.GetFrameHeight() : 0f;

        // Hors de la zone de contenu : la liste de la fenêtre est rognée en deçà
        // des marges, et le fond n'irait pas jusqu'au bord.
        dl.PushClipRect(position, position + size, false);
        Surface.NightBackground(dl, position + new Vector2(0f, top), position + size,
                                Theme.S(Theme.RadiusWindow), roundTop: titled is false,
                                opacity: BackgroundOpacity);
        dl.PopClipRect();
    }
```

Le commentaire de `BackgroundOpacity` reste vrai (« légèrement translucide, avec le flou natif de Dalamud derrière »).

Dans `MainWindow.cs`, `RequestToasts.cs`, `GroupEntryWindow.cs` et `Onboarding/OnboardingWindow.cs`, remplacer `public override void Draw()` par `protected override void DrawContents()`. Rien d'autre ne change dans ces méthodes.

- [ ] **Étape 4 : cartes translucides, halo à la place de la barre d'accent**

Dans `Card.cs`, remplacer :

```csharp
        var bg = background ?? (tone == CardTone.Interactive && hovered ? Theme.BgRaised : Theme.BgSurface);

        var dl = ImGui.GetWindowDrawList();
        Surface.Panel(dl, min, max, bg, border ?? Theme.Border);

        if (accent is { } accentColor)
            Surface.AccentBar(dl, min, max, accentColor);
```

par :

```csharp
        var bg = background ?? (tone == CardTone.Interactive && hovered ? Theme.CardFillHover : Theme.CardFill);

        var dl       = ImGui.GetWindowDrawList();
        var rounding = Theme.S(Theme.RadiusCard);

        // Une carte active se signale par un halo tout autour, comme le champ
        // du site, et non plus par une barre à gauche. Peint avant elle : son
        // fond recouvre l'intérieur. Sans ombre, qui assombrirait le halo.
        if (accent is { } accentColor)
        {
            Surface.Glow(dl, min, max, rounding, accentColor);
            Surface.Panel(dl, min, max, bg, Theme.Alpha(accentColor, 0.70f), rounding, shadow: false);
        }
        else
        {
            Surface.Panel(dl, min, max, bg, border ?? Theme.Border, rounding);
        }
```

- [ ] **Étape 5 : le bandeau de titre dans la nuit**

Dans `TitleBar.cs`, remplacer depuis le commentaire `// Bandeau dans le ton foncé de l'accent` jusqu'à `dl.AddRectFilledMultiColor(origin, end, glow, clear, clear, glow);` inclus par :

```csharp
        // Bandeau un cran sous la nuit, comme les barres du site : c'est le
        // contenu qui porte la lumière, pas le chrome. Le rayon est celui de la
        // fenêtre, sinon le fond déborde aux angles.
        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Theme.BgSidebar),
            Theme.S(Theme.RadiusWindow), ImDrawFlags.RoundCornersTop);

        dl.AddLine(new Vector2(origin.X, end.Y - 0.5f), new Vector2(end.X, end.Y - 0.5f),
            ImGui.GetColorU32(Theme.Border), 1f);
```

- [ ] **Étape 6 : la perle de connexion**

Dans `StatusBar.cs`, remplacer le commentaire `// Pastille d'état, à gauche.` et le `dl.AddCircleFilled(...)` qui le suit par :

```csharp
        // Pastille d'état, à gauche : la perle quand le service répond, un
        // point éteint sinon. Sans halo, la barre est trop basse pour lui.
        var dot = new Vector2(origin.X + Theme.S(Theme.PadWindowX), mid);

        if (status.Connected)
            Surface.Pearl(dl, dot, Theme.S(4.5f), glow: false);
        else
            dl.AddCircleFilled(dot, Theme.S(3.5f), ImGui.GetColorU32(Theme.TextFaint));
```

- [ ] **Étape 7 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `0 Warning(s)`, `0 Error(s)`.
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : PASS.

- [ ] **Étape 8 : déployer**

Run : `./scripts/deploy-plugin-dev.sh`

- [ ] **Étape 9 : commit**

```bash
git add Linkpearl/Ui
git commit -m "feat(ui): la nuit du site, son halo et sa perle"
```

- [ ] **Étape 10 : POINT D'ARRÊT, l'utilisateur juge en jeu**

Ne pas commencer la tâche 4 avant sa réponse. Lui demander de recharger le plugin et de regarder :
- le fond de la fenêtre principale : dégradé et halo bleu en haut du contenu, sans marches visibles ;
- la page Groupes, **Public activé** : halo autour de la carte, titres en Fredoka ;
- la barre d'état connectée : la perle à gauche ;
- les fenêtres Rejoindre un groupe et présentation : la nuit sous le titre, le titre natif intact ;
- à l'échelle Dalamud 150 % (réglages de Dalamud, Interface) : rien de rogné ni de décalé.

Ses retours se traitent ici, un commit `fix(ui):` par retouche : position et rayons du halo dans `NightBackground`, intensité (`0.30f`) et `spread` de `Glow`, proportions de `Pearl`, tailles de `Fonts.Build`. Si le halo ou la perle font bon marché malgré les retouches, les retirer : `NightBackground` garde le seul dégradé, `Pearl` devient un disque `Theme.PearlBody`.

---

### Tâche 4 : les tons de bouton, l'orange à l'action

**Fichiers :**
- Modifier : `Linkpearl/Ui/Components/Btn.cs` (tons, pilules, liserés)
- Modifier : `Linkpearl/Ui/Components/Text.cs` (`Label`)
- Modifier les usages de `BtnTone.Primary`, et les « Accepter » en `BtnTone.Success` : `GroupEntryWindow.cs`, `Onboarding/OnboardingWindow.cs`, `Pages/BackupCard.cs`, `Pages/GroupsPage.cs`, `Pages/NearbyPage.cs`, `Pages/SettingsPage.cs`, `Pages/RequestsPage.cs`, `RequestToasts.cs`
- Modifier : `Pages/GroupsPage.cs` (`Section` remplacé par `Text.Label`)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs` (règle `BtnTone.Primary`)

**Interfaces :**
- Produit : `enum BtnTone { Action, Selected, Secondary, Ghost, Danger, Success }`. `Primary` n'existe plus.
- Produit : `Text.Label(string text)`, titre de section en petites capitales `TextFaint`.

- [ ] **Étape 1 : la règle qui échoue**

Dans `UiConventionTests.Rules`, ajouter :

```csharp
        {
            "BtnTone.Primary", "l'orange est à l'action (BtnTone.Action), le choix au halo (BtnTone.Selected)", [], []
        },
```

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL, « BtnTone.Primary interdit » dans `Components.Btn.cs`, `GroupEntryWindow.cs`, `Onboarding.OnboardingWindow.cs`, `Pages.BackupCard.cs`, `Pages.GroupsPage.cs`, `Pages.NearbyPage.cs`, `Pages.SettingsPage.cs`.

- [ ] **Étape 2 : les tons**

Dans `Btn.cs`, l'énumération devient :

```csharp
internal enum BtnTone
{
    /// <summary>L'action principale de l'écran, en orange pompon. Une seule par vue.</summary>
    Action,

    /// <summary>L'option choisie dans un groupe de boutons, teintée du halo.</summary>
    Selected,

    /// <summary>Action courante, surface translucide bordée.</summary>
    Secondary,

    /// <summary>Action discrète : pas de fond, sauf au survol.</summary>
    Ghost,

    /// <summary>Action destructrice.</summary>
    Danger,

    /// <summary>Action de confirmation.</summary>
    Success,
}
```

Dans le commentaire de classe de `Btn`, remplacer « même sur l'accent nacré, qui est clair » par « même sur l'orange, qui est clair ».

`Btn.Draw` devient :

```csharp
    public static bool Draw(string label,
                            BtnTone tone = BtnTone.Secondary,
                            BtnSize size = BtnSize.Medium,
                            FontAwesomeIcon? icon = null,
                            bool disabled = false,
                            string? tooltip = null,
                            string? id = null)
    {
        var (normal, hovered, active) = Palette(tone);
        var caption = Compose(label, icon);

        using var color = ImRaii.PushColor(ImGuiCol.Button, normal)
                                .Push(ImGuiCol.ButtonHovered, hovered)
                                .Push(ImGuiCol.ButtonActive,  active)
                                .Push(ImGuiCol.Text,          TextFor(tone, normal));

        // En pilule, comme les boutons du site : le rayon vaut la moitié de la
        // hauteur, ce qui reste juste à toute échelle. Le contour des champs de
        // saisie ne doit pas déborder sur les boutons, d'où la bordure nulle.
        var rounding = ImGui.GetFrameHeight() * 0.5f;

        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 0f)
                                .Push(ImGuiStyleVar.FrameRounding, rounding);

        bool clicked;

        using (ImRaii.Disabled(disabled))
        {
            clicked = ImGui.Button($"{caption}##{id ?? label}", Dimensions(size, caption));
            Outline(tone, rounding);
        }

        // Hors de la portée désactivée : un widget désactivé ne remonte pas le survol.
        if (tooltip != null)
            Feedback.TooltipOnHover(tooltip);

        return clicked && disabled is false;
    }
```

Dans `Btn.Icon`, remplacer `.Push(ImGuiCol.Text, Theme.TextOn(normal));` par `.Push(ImGuiCol.Text, TextFor(tone, normal));`, et ajouter `Outline(tone, Theme.S(Theme.RadiusFrame));` juste après l'appel `ImGui.GetWindowDrawList().AddText(...)` du glyphe, dans la portée désactivée. `Btn.Icon` garde son rayon de cadre : une pilule carrée serait un rond, et une ligne de ronds ne s'aligne plus sur les boutons voisins.

Remplacer `Palette` par :

```csharp
    private static (Vector4 Normal, Vector4 Hovered, Vector4 Active) Palette(BtnTone tone) => tone switch
    {
        BtnTone.Action   => (Theme.Action, Theme.ActionHover, Theme.ActionActive),
        BtnTone.Selected => (Theme.Alpha(Theme.Accent, 0.20f), Theme.Alpha(Theme.Accent, 0.30f),
                             Theme.Alpha(Theme.Accent, 0.38f)),
        BtnTone.Danger   => (Theme.Danger, Theme.DangerHover, Theme.Mix(Theme.Danger, Theme.BgBase, 0.3f)),
        BtnTone.Success  => (Theme.Online, Theme.Mix(Theme.Online, Theme.Text, 0.2f),
                             Theme.Mix(Theme.Online, Theme.BgBase, 0.3f)),
        BtnTone.Ghost    => (Vector4.Zero, Theme.Alpha(Theme.Text, 0.08f), Theme.Alpha(Theme.Text, 0.12f)),
        _                => (Theme.Alpha(Theme.Text, 0.06f), Theme.Alpha(Theme.Text, 0.11f),
                             Theme.Alpha(Theme.Text, 0.04f)),
    };

    /// <summary>
    /// Couleur du libellé.
    /// </summary>
    /// <remarks>
    /// Les tons translucides sont du blanc à quelques pour cent : leur
    /// luminance, calculée sur la seule couleur, les ferait passer pour clairs
    /// et leur donnerait un texte sombre, illisible sur la nuit.
    /// </remarks>
    private static Vector4 TextFor(BtnTone tone, Vector4 background) => tone switch
    {
        BtnTone.Action                                         => Theme.TextOnAction,
        BtnTone.Selected or BtnTone.Secondary or BtnTone.Ghost => Theme.Text,
        _                                                      => Theme.TextOn(background),
    };

    /// <summary>
    /// Liseré des tons translucides.
    /// </summary>
    /// <remarks>
    /// Sans lui, un bouton secondaire posé sur une carte translucide n'a plus de
    /// bord, et un bouton choisi ne se distingue que par une teinte.
    /// </remarks>
    private static void Outline(BtnTone tone, float rounding)
    {
        Vector4? line = tone switch
        {
            BtnTone.Selected  => Theme.Accent,
            BtnTone.Secondary => Theme.Border,
            _                 => null,
        };

        if (line is { } color)
            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(),
                ImGui.GetColorU32(color), rounding, ImDrawFlags.None, 1f);
    }
```

- [ ] **Étape 3 : reclasser chaque usage**

Chaque changement porte sur le seul argument de ton.

| Fichier | Bouton | Avant | Après | Pourquoi |
|---|---|---|---|---|
| `GroupEntryWindow.cs` | Rejoindre | `Primary` | `Action` | valide la fenêtre |
| `GroupEntryWindow.cs` | Créer | `Primary` | `Action` | valide la fenêtre |
| `Onboarding/OnboardingWindow.cs` | Suivant / C'est parti | `Primary` | `Action` | fait avancer la présentation |
| `Pages/BackupCard.cs` | Sauvegarder… | `Primary` | `Action` | l'action de la carte |
| `Pages/BackupCard.cs` | Restaurer | `Primary` | `Action` | valide la restauration |
| `Pages/GroupsPage.cs` | Activer / Désactiver (`public_toggle`) | `enabled ? Secondary : Primary` | `BtnTone.Secondary` | l'état se lit au halo de la carte, l'action de la page est ailleurs |
| `Pages/GroupsPage.cs` | effets de Public (`public_fx_`) | `on ? Primary : Secondary` | `on ? Selected : Secondary` | un choix, pas une action |
| `Pages/GroupsPage.cs` | Activer Public (`public_confirm`) | `Primary` | `Action` | confirme l'avertissement |
| `Pages/GroupsPage.cs` | Rejoindre un groupe (`open_join`) | `Primary` | `Action` | l'action de la page |
| `Pages/GroupsPage.cs` | Mot de passe (`mode_password`) | `password ? Primary : Secondary` | `password ? Selected : Secondary` | le mode choisi |
| `Pages/GroupsPage.cs` | Validation par un modérateur (`mode_validation`) | `password ? Secondary : Primary` | `password ? Secondary : Selected` | le mode choisi |
| `Pages/NearbyPage.cs` | Demander | `sent ? Secondary : Primary` | `sent ? Secondary : Action` | l'action d'une ligne à pairer |
| `Pages/SettingsPage.cs` | Ajouter les N cochés | `Primary` | `Action` | valide la découverte |
| `Pages/RequestsPage.cs` (×2) | Accepter | `Success` | `Action` | l'action de la carte |
| `RequestToasts.cs` (×2) | Accepter | `Success` | `Action` | l'action de la carte |

- [ ] **Étape 4 : les titres de section**

Dans `Text.cs`, ajouter après `Small` :

```csharp
    /// <summary>
    /// Titre de section à l'intérieur d'une carte, à la manière des libellés du
    /// site : petites capitales discrètes.
    /// </summary>
    /// <remarks>
    /// Sans icône : un titre de section n'est pas une action, et les icônes de
    /// la carte sont celles de ses boutons.
    /// </remarks>
    public static void Label(string text)
    {
        Small(text.ToUpperInvariant(), Theme.TextFaint);
        ImGui.Dummy(new Vector2(0f, Theme.S(Theme.GapXs)));
    }
```

Dans `GroupsPage.cs`, remplacer chaque appel `Section(Icons.Xxx, "titre");` par `Text.Label("titre");` :

```bash
sed -i -E 's/Section\(Icons\.[A-Za-z]+, (\$?"[^"]*")\);/Text.Label(\1);/' Linkpearl/Ui/Pages/GroupsPage.cs
```

Puis supprimer la méthode `private static void Section(FontAwesomeIcon icon, string title)` et son commentaire.

Run : `grep -n "Section(" Linkpearl/Ui/Pages/GroupsPage.cs`
Attendu : aucune ligne.

- [ ] **Étape 5 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `0 Warning(s)`, `0 Error(s)`. Une erreur sur `BtnTone.Primary` désigne un usage oublié.
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : PASS.

- [ ] **Étape 6 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu : « Rejoindre un groupe » et « Demander » en orange, texte brun ; le mode d'admission choisi en halo bordé ; les boutons secondaires bordés, en pilule ; les boutons d'icône des pairs toujours carrés ; « MEMBRES » en petites capitales dans une carte de groupe.

- [ ] **Étape 7 : commit**

```bash
git add Linkpearl/Ui Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): l'orange à l'action, le halo au choix, boutons en pilule"
```

---

### Tâche 5 : le cadre et le logo

**Fichiers :**
- Créer : `Linkpearl/Assets/Images/logo.png` (copie de `Plugin_Logo.png`)
- Créer : `Linkpearl/Ui/Brand.cs`
- Modifier : `Linkpearl/Linkpearl.csproj` (ressource du logo)
- Modifier : `Linkpearl/Plugin.cs` (après `Fonts.Build(PluginInterface);` et avant `Fonts.Dispose();`)
- Modifier : `Linkpearl/Ui/Shell/TitleBar.cs` (bloc « Marque »)
- Modifier : `Linkpearl/Ui/Shell/Sidebar.cs` (entrée active)
- Modifier : `Linkpearl/Ui/Components/Feedback.cs` (`EmptyState`, `Centered`)

**Interfaces :**
- Produit : `Brand.Initialize(ISharedImmediateTexture logo)`, `Brand.Dispose()`, `bool Brand.Draw(ImDrawListPtr dl, Vector2 min, float side, bool glow = false)`, qui rend `false` sans rien dessiner tant que la texture n'est pas chargée.

- [ ] **Étape 1 : embarquer le logo**

```bash
cp Plugin_Logo.png Linkpearl/Assets/Images/logo.png
```

Dans `Linkpearl.csproj`, dans l'`ItemGroup` de la bannière, ajouter sous elle :

```xml
    <!-- Le logo du site, pour la barre de titre et les pages vides. -->
    <EmbeddedResource Include="Assets\Images\logo.png" LogicalName="Images.logo.png" />
```

- [ ] **Étape 2 : `Brand`**

`Linkpearl/Ui/Brand.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// Le logo du plugin, celui du site : deux mogs et leur perle.
/// </summary>
/// <remarks>
/// Statique comme <see cref="Fonts"/> : la barre de titre et les pages vides
/// sont des aides statiques, et leur passer la texture de main en main
/// traverserait toutes les pages.
///
/// Tant que la texture n'est pas chargée, rien n'est dessiné et
/// <see cref="Draw"/> rend faux : l'appelant garde alors sa place ou son icône.
/// </remarks>
internal static class Brand
{
    private static ISharedImmediateTexture? _logo;

    public static void Initialize(ISharedImmediateTexture logo) => _logo = logo;

    public static void Dispose() => _logo = null;

    /// <summary>Dessine le logo dans le carré donné. Rend faux s'il n'est pas encore chargé.</summary>
    /// <param name="glow">Un halo derrière, pour les grandes tailles.</param>
    public static bool Draw(ImDrawListPtr dl, Vector2 min, float side, bool glow = false)
    {
        if (_logo?.GetWrapOrDefault() is not { } wrap)
            return false;

        if (glow)
            Surface.Halo(dl, min + new Vector2(side * 0.5f), new Vector2(side * 0.8f),
                         Theme.Alpha(Theme.Accent, 0.35f));

        dl.AddImage(wrap.Handle, min, min + new Vector2(side, side));
        return true;
    }
}
```

- [ ] **Étape 3 : le charger et le libérer**

Dans `Plugin.cs`, juste après `Fonts.Build(PluginInterface);` :

```csharp
        Brand.Initialize(Textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "Images.logo.png"));
```

Juste avant `Fonts.Dispose();` :

```csharp
        Brand.Dispose();
```

- [ ] **Étape 4 : le logo dans la barre de titre**

Dans `TitleBar.cs`, remplacer tout le bloc qui va de `// ── Marque ──` jusqu'à la ligne vide qui précède `// ── Fermeture ──` par :

```csharp
        // ── Marque ────────────────────────────────────────────────────────────
        // Le logo du site en pastille, puis le nom en Fredoka : le plugin se
        // reconnaît à ce qu'on a vu en l'installant.
        using (Fonts.PushH2())
        {
            const string text = "Linkpearl";

            var logo = MathF.Round(height - Theme.S(Theme.GapM) * 2f);
            var x    = origin.X + Theme.S(Theme.PadWindowX);

            if (Brand.Draw(dl, new Vector2(x, MathF.Round(origin.Y + (height - logo) * 0.5f)), logo))
                x += logo + Theme.S(Theme.GapS);

            var textSize = ImGui.CalcTextSize(text);

            dl.AddText(new Vector2(x, origin.Y + (height - textSize.Y) * 0.5f),
                ImGui.GetColorU32(Theme.Text), text);
        }

```

Si `using Dalamud.Game.Text;` n'a plus d'usage dans le fichier, le retirer.

- [ ] **Étape 5 : l'entrée active de la barre latérale**

Dans `Sidebar.DrawItem`, remplacer les deux blocs `if (active || hovered) { ... }` et `if (active) { ... }` (la barre verticale) par :

```csharp
        if (active || hovered)
        {
            var inset = Theme.S(Theme.GapM);
            var min   = origin + new Vector2(inset, Theme.S(2f));
            var max   = origin + new Vector2(width - inset, item - Theme.S(2f));
            var r     = Theme.S(Theme.RadiusCard);

            // L'entrée active dans le halo, bordée comme le bouton de langue
            // choisi sur le site ; le survol, à peine éclairé.
            if (active)
                Surface.Glow(dl, min, max, r, Theme.Accent, spread: 5f);

            dl.AddRectFilled(min, max, ImGui.GetColorU32(active
                ? Theme.Alpha(Theme.Accent, 0.18f)
                : Theme.Alpha(Theme.Text, 0.06f)), r);

            if (active)
                dl.AddRect(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Accent, 0.80f)), r, ImDrawFlags.None, 1f);
        }
```

La teinte devient :

```csharp
        var tint = ImGui.GetColorU32(active || hovered ? Theme.Text : Theme.TextMuted);
```

Le résumé de la classe dit « l'entrée active signalée par un halo et un liseré » au lieu de « une pastille de fond et un liseré d'accent à gauche ».

- [ ] **Étape 6 : le logo sur les pages vides**

Dans `Feedback.cs`, `EmptyState` devient :

```csharp
    public static void EmptyState(FontAwesomeIcon icon, string title, string? hint = null)
    {
        ImGui.Dummy(new Vector2(0f, Theme.S(Theme.GapXl)));

        var width = ImGui.GetContentRegionAvail().X;
        var side  = Theme.S(72f);
        var at    = ImGui.GetCursorScreenPos() + new Vector2((width - side) * 0.5f, 0f);

        // Le logo quand il est là : une page vide est le moment où le plugin se
        // présente. L'icône de la page sinon, le temps que la texture arrive.
        if (Brand.Draw(ImGui.GetWindowDrawList(), at, side, glow: true))
        {
            ImGui.Dummy(new Vector2(0f, side));
        }
        else
        {
            using var font = Fonts.PushTitle();

            var glyph = icon.S();
            var size  = ImGui.CalcTextSize(glyph);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (width - size.X) * 0.5f);
            ImGui.TextColored(Theme.TextFaint, glyph);
        }

        ImGui.Dummy(new Vector2(0f, Theme.S(Theme.GapM)));

        Centered(title, Theme.Text, heading: true);

        if (hint is null)
            return;

        ImGui.Dummy(new Vector2(0f, Theme.S(Theme.GapXs)));
        Centered(hint, Theme.TextFaint, small: true);
    }
```

Et la première ligne de `Centered` devient :

```csharp
    private static void Centered(string text, Vector4 color, bool small = false, bool heading = false)
    {
        using var font = heading ? Fonts.PushH2() : small ? Fonts.PushSmall() : Fonts.PushBody();
```

Le reste de `Centered` ne change pas.

- [ ] **Étape 7 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `0 Warning(s)`, `0 Error(s)`.
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : PASS.

- [ ] **Étape 8 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu : le logo rond à gauche de « Linkpearl » dans la barre de titre ; l'entrée active de la barre latérale dans le halo ; une page vide (Demandes sans demande) avec le logo et son halo, le titre en Fredoka ; à 150 %, le logo grandit avec la barre.

- [ ] **Étape 9 : commit**

```bash
git add Linkpearl/Assets/Images/logo.png Linkpearl/Linkpearl.csproj Linkpearl/Plugin.cs Linkpearl/Ui
git commit -m "feat(ui): le logo dans la barre de titre et sur les pages vides"
```

---

### Tâche 6 : Autour de vous, et les en-têtes repliables

**Fichiers :**
- Créer : `Linkpearl/Ui/Components/Fold.cs`
- Modifier : `Linkpearl/Ui/Pages/NearbyPage.cs` (`Group`)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`

**Interfaces :**
- Produit : `bool Fold.Draw(string title, int count, string id, bool defaultOpen = true)`, vrai si le contenu doit être dessiné. L'état ouvert ou fermé est gardé par identifiant pour la session.

- [ ] **Étape 1 : la règle qui échoue**

Dans `UiConventionTests.Rules`, retirer `"Pages.NearbyPage.cs"` des fichiers en attente de `ImGui.CollapsingHeader(`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL, « ImGui.CollapsingHeader( interdit » dans `Pages.NearbyPage.cs`.

- [ ] **Étape 2 : `Fold`**

`Linkpearl/Ui/Components/Fold.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// En-tête repliable : chevron, titre, compteur.
/// </summary>
/// <remarks>
/// Remplace <c>CollapsingHeader</c>, dont le bandeau gris plein cadre jurait
/// avec la nuit et les cartes translucides. Ici, rien qu'un survol à peine
/// éclairé : l'en-tête range une liste, il n'est pas une surface.
///
/// L'état est gardé par identifiant, pour la session : une liste repliée le
/// reste d'une image à l'autre, et d'une ouverture de fenêtre à l'autre.
/// </remarks>
internal static class Fold
{
    private static readonly Dictionary<uint, bool> Open = [];

    /// <summary>Dessine l'en-tête. Rend vrai si le contenu doit être dessiné dessous.</summary>
    public static bool Draw(string title, int count, string id, bool defaultOpen = true)
    {
        var key = ImGui.GetID(id);

        if (Open.TryGetValue(key, out var open) is false)
        {
            open = defaultOpen;
            Open[key] = open;
        }

        var start  = ImGui.GetCursorScreenPos();
        var width  = Math.Max(1f, ImGui.GetContentRegionAvail().X - Card.RightInset);
        var height = ImGui.GetFrameHeight();

        if (ImGui.InvisibleButton($"##fold_{id}", new Vector2(width, height)))
        {
            open = !open;
            Open[key] = open;
        }

        var hovered = ImGui.IsItemHovered();
        var dl      = ImGui.GetWindowDrawList();

        if (hovered)
        {
            dl.AddRectFilled(start, start + new Vector2(width, height),
                ImGui.GetColorU32(Theme.Alpha(Theme.Text, 0.06f)), Theme.S(Theme.RadiusFrame));

            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        var chevron = (open ? Icons.Expanded : Icons.Collapsed).S();
        var label   = Glyphs.Safe(title);
        var counter = $"{count}";

        var mid = start.Y + height * 0.5f;
        var x   = start.X + Theme.S(Theme.GapS);

        var chevronSize = ImGui.CalcTextSize(chevron);
        dl.AddText(new Vector2(x, mid - chevronSize.Y * 0.5f), ImGui.GetColorU32(Theme.TextFaint), chevron);

        // Le titre démarre à une abscisse fixe : les chevrons ouvert et fermé
        // n'ont pas la même largeur, et le titre sautait à chaque clic.
        x += ImGui.CalcTextSize(Icons.Expanded.S()).X + Theme.S(Theme.GapM);

        var labelSize = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(x, mid - labelSize.Y * 0.5f), ImGui.GetColorU32(Theme.Text), label);

        x += labelSize.X + Theme.S(Theme.GapS);

        using (Fonts.PushSmall())
        {
            var counterSize = ImGui.CalcTextSize(counter);
            dl.AddText(new Vector2(x, mid - counterSize.Y * 0.5f), ImGui.GetColorU32(Theme.TextFaint), counter);
        }

        return open;
    }
}
```

- [ ] **Étape 3 : l'utiliser dans Autour de vous**

Dans `NearbyPage.Group`, remplacer :

```csharp
        using var header = ImRaii.PushColor(ImGuiCol.Header, Theme.BgSurface)
                                 .Push(ImGuiCol.HeaderHovered, Theme.BgRaised)
                                 .Push(ImGuiCol.HeaderActive, Theme.BgRaised);

        if (ImGui.CollapsingHeader($"{title} ({players.Count})###groupe_{title}", ImGuiTreeNodeFlags.DefaultOpen) is false)
            return;
```

par :

```csharp
        if (Fold.Draw(title, players.Count, $"autour_{title}") is false)
            return;
```

- [ ] **Étape 4 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests` (attendu : PASS)

- [ ] **Étape 5 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, près de l'autre personnage : « À pairer » et « Déjà pairés » sans bandeau gris, le chevron fixe, le titre qui ne saute pas au clic ; replier, fermer la fenêtre, rouvrir : la liste reste repliée.

- [ ] **Étape 6 : commit**

```bash
git add Linkpearl/Ui/Components/Fold.cs Linkpearl/Ui/Pages/NearbyPage.cs Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): en-têtes repliables sans bandeau, Autour de vous"
```

---

### Tâche 7 : Pairs, et le choix des effets

**Fichiers :**
- Créer : `Linkpearl/Ui/Components/EffectsPicker.cs`
- Modifier : `Linkpearl/Ui/Pages/PairsPage.cs` (`Group` et `DrawReceive`)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`

**Interfaces :**
- Consomme : `Fold.Draw` (tâche 6), `BtnTone.Selected` (tâche 4).
- Produit : `TransientCategories? EffectsPicker.Draw(TransientCategories current, string id)`, qui rend les nouvelles catégories quand un bouton vient d'être cliqué, `null` sinon.

- [ ] **Étape 1 : les règles qui échouent**

Dans `UiConventionTests.Rules`, retirer `"Pages.PairsPage.cs"` des fichiers en attente de `ImGui.Checkbox(` et de `ImGui.CollapsingHeader(`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL sur les deux règles, dans `Pages.PairsPage.cs`.

- [ ] **Étape 2 : `EffectsPicker`**

`Linkpearl/Ui/Components/EffectsPicker.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Linkpearl.Core.Safety;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Animations, VFX et sons, en trois boutons allumés ou éteints.
/// </summary>
/// <remarks>
/// Des boutons et non des cases : l'état se lit à la couleur, chaque catégorie
/// porte l'icône qu'elle a dans la barre de titre, et les trois tiennent sur une
/// ligne. Le même composant sert au Public, à un pair et à un membre de groupe,
/// pour qu'un joueur n'apprenne le geste qu'une fois.
/// </remarks>
internal static class EffectsPicker
{
    /// <summary>Dessine les trois boutons. Rend les catégories modifiées, ou null si rien n'a été cliqué.</summary>
    public static TransientCategories? Draw(TransientCategories current, string id)
    {
        (string Label, FontAwesomeIcon Icon, bool On, Func<bool, TransientCategories> With)[] toggles =
        [
            ("Animations", Icons.Animations, current.Animations, on => current with { Animations = on }),
            ("VFX",        Icons.Vfx,        current.Vfx,        on => current with { Vfx = on }),
            ("Sons",       Icons.Sounds,     current.Sounds,     on => current with { Sounds = on }),
        ];

        TransientCategories? changed = null;

        for (var i = 0; i < toggles.Length; i++)
        {
            var (label, icon, on, with) = toggles[i];

            if (i > 0)
                ImGui.SameLine(0f, Theme.S(Theme.GapXs));

            if (Btn.Draw(label, on ? BtnTone.Selected : BtnTone.Secondary, BtnSize.Small, icon, id: $"{id}_{i}",
                         tooltip: on ? "Reçus. Cliquer pour les bloquer." : "Bloqués. Cliquer pour les recevoir."))
                changed = with(on is false);
        }

        return changed;
    }
}
```

- [ ] **Étape 3 : l'en-tête des groupes de pairs**

Dans `PairsPage.Group`, remplacer :

```csharp
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        using var header = ImRaii.PushColor(ImGuiCol.Header, Theme.BgSurface)
                                 .Push(ImGuiCol.HeaderHovered, Theme.BgRaised)
                                 .Push(ImGuiCol.HeaderActive, Theme.BgRaised);

        if (ImGui.CollapsingHeader($"{title} ({pairs.Count})###groupe_{title}", flags) is false)
            return;
```

par :

```csharp
        if (Fold.Draw(title, pairs.Count, $"pairs_{title}", defaultOpen) is false)
            return;
```

- [ ] **Étape 4 : la popup d'effets d'un pair**

Dans `PairsPage.DrawReceive`, remplacer depuis `var animations = receive.Animations;` jusqu'à `setReceive(pair.Id, new TransientCategories(animations, vfx, sounds));` inclus par :

```csharp
        if (EffectsPicker.Draw(receive, $"fx_{id}") is { } changed)
            setReceive(pair.Id, changed);
```

- [ ] **Étape 5 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur ; retirer les `using` devenus inutiles s'il en reste)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests` (attendu : PASS)

- [ ] **Étape 6 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, page Pairs : « En pause » et « Bloqués » repliés par défaut comme avant ; le bouton d'effets d'un pair ouvre trois boutons, un clic bloque les sons et le bouton passe en éteint, l'icône d'effets du pair s'accentue.

- [ ] **Étape 7 : commit**

```bash
git add Linkpearl/Ui/Components/EffectsPicker.cs Linkpearl/Ui/Pages/PairsPage.cs Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): Pairs, effets en boutons et en-têtes repliables"
```

---

### Tâche 8 : Groupes et Public

**Fichiers :**
- Modifier : `Linkpearl/Ui/Pages/GroupsPage.cs` (effets de Public, joueurs rencontrés, popup d'effets d'un membre)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`

**Interfaces :**
- Consomme : `EffectsPicker.Draw` (tâche 7), `Fold.Draw` (tâche 6), `Text.Label` (tâche 4).

- [ ] **Étape 1 : la règle qui échoue**

Retirer `"Pages.GroupsPage.cs"` des fichiers en attente de `ImGui.Checkbox(`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL, `ImGui.Checkbox(` dans `Pages.GroupsPage.cs`.

- [ ] **Étape 2 : les effets de Public**

`DrawPublicEffects` et son commentaire deviennent :

```csharp
    /// <summary>Les effets reçus des joueurs du Public que l'on n'a pas réglés un par un.</summary>
    private void DrawPublicEffects(GroupRecord @public)
    {
        Text.Label("Effets reçus");

        if (EffectsPicker.Draw(@public.DefaultReceive, "public_fx") is { } changed)
            actions.SetDefaultReceive(PublicGroup.Id, changed);

        ImGui.Dummy(Theme.S(0f, Theme.GapXs));
        Text.Small("Pour les joueurs que vous n'avez pas réglés un par un.", Theme.TextFaint);
    }
```

- [ ] **Étape 3 : les joueurs rencontrés**

Supprimer le champ `_publicMembersOpen` et son commentaire. `DrawPublicMembers` devient :

```csharp
    /// <summary>Les joueurs du Public déjà croisés, repliés par défaut.</summary>
    /// <remarks>Dans une foule, la liste dépliée repousserait les groupes privés hors de l'écran.</remarks>
    private void DrawPublicMembers(GroupRecord @public)
    {
        if (Fold.Draw("Joueurs rencontrés", @public.Members.Count, "public_members", defaultOpen: false) is false)
            return;

        using var scope = ImRaii.PushId("public");
        DrawMembers(@public, GroupRole.Member, statuses());
    }
```

- [ ] **Étape 4 : la popup d'effets d'un membre**

Dans `DrawReceive(GroupRecord group, GroupMember member, string id)`, remplacer depuis `var animations = receive.Animations;` jusqu'à `actions.SetReceive(group.Id, member.Fingerprint, new TransientCategories(animations, vfx, sounds));` inclus par :

```csharp
        if (EffectsPicker.Draw(receive, $"fx_{id}") is { } changed)
            actions.SetReceive(group.Id, member.Fingerprint, changed);
```

Le `/// <remarks>Copie de celui des pairs...` devient `/// <remarks>Le même que celui des pairs : accentué dès qu'une catégorie est bloquée.</remarks>`.

- [ ] **Étape 5 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests` (attendu : PASS)

- [ ] **Étape 6 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, page Groupes : carte Public activée dans le halo, « EFFETS REÇUS » puis les trois boutons, « Joueurs rencontrés » replié ; créer un groupe privé nommé « Łódź » : le nom entier s'affiche dans l'en-tête de sa carte, rien en `?` (point de vigilance 5).

- [ ] **Étape 7 : commit**

```bash
git add Linkpearl/Ui/Pages/GroupsPage.cs Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): Groupes et Public sur les composants communs"
```

---

### Tâche 9 : Demandes et notifications

**Fichiers :**
- Modifier : `Linkpearl/Ui/Pages/RequestsPage.cs`
- Modifier : `Linkpearl/Ui/RequestToasts.cs`
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`

- [ ] **Étape 1 : la règle qui échoue**

Retirer `"Pages.RequestsPage.cs"` et `"RequestToasts.cs"` des fichiers en attente de `ImGui.SameLine()`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL, `ImGui.SameLine()` dans ces deux fichiers.

- [ ] **Étape 2 : l'espacement entre Accepter et Refuser**

Dans les deux fichiers, chaque `ImGui.SameLine();` placé entre « Accepter » et « Refuser » devient, à la même indentation :

```csharp
ImGui.SameLine(0f, Theme.S(Theme.GapS));
```

- [ ] **Étape 3 : des notifications opaques**

Dans `RequestToasts.cs`, les deux `Card.Begin` deviennent :

```csharp
        // Opaque : la fenêtre n'a pas de fond et flotte sur le décor du jeu,
        // une carte translucide s'y lirait mal sur la neige ou le sable.
        using var card = Card.Begin($"toast_{id}", background: Theme.BgSurface, accent: Theme.Accent);
```

et

```csharp
        using var card = Card.Begin($"toast_admission_{id}", background: Theme.BgSurface, accent: Theme.Accent);
```

(le commentaire une seule fois, au-dessus du premier).

- [ ] **Étape 4 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests` (attendu : PASS)

- [ ] **Étape 5 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, demander un pairage depuis l'autre personnage : la notification en carte opaque dans le halo, « Accepter » en orange ; se placer devant un décor clair (point de vigilance 3) : le texte reste lisible ; la page Demandes montre la même carte, translucide sur la nuit.

- [ ] **Étape 6 : commit**

```bash
git add Linkpearl/Ui/Pages/RequestsPage.cs Linkpearl/Ui/RequestToasts.cs Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): demandes et notifications, opaques sur le décor du jeu"
```

---

### Tâche 10 : Réglages, et l'interrupteur

**Fichiers :**
- Créer : `Linkpearl/Ui/Components/Toggle.cs`
- Modifier : `Linkpearl/Ui/Pages/SettingsPage.cs` (visibilité, réseau, services, découverte)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`

**Interfaces :**
- Consomme : `Surface.Glow`, `Surface.Pearl` (tâche 3).
- Produit :
  - `bool Toggle.Draw(string label, ref bool value, string id, string? hint = null, Action? hintContent = null)` : rangée complète, libellé à gauche, interrupteur au bord droit ; rend vrai au clic.
  - `bool Toggle.Switch(ref bool value, string id, string? tooltip = null)` : l'interrupteur seul, en ligne ; rend vrai au clic.

- [ ] **Étape 1 : les règles qui échouent**

Retirer `"Pages.SettingsPage.cs"` des fichiers en attente de `ImGui.Checkbox(` et de `ImGui.SameLine()`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL sur les deux règles, dans `Pages.SettingsPage.cs`.

- [ ] **Étape 2 : `Toggle`**

`Linkpearl/Ui/Components/Toggle.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Interrupteur : le réglage oui/non du plugin.
/// </summary>
/// <remarks>
/// Libellé à gauche, interrupteur calé au bord droit : d'un réglage à l'autre,
/// les interrupteurs s'alignent en colonne et se comparent d'un coup d'œil, ce
/// que des cases collées à des libellés de longueurs diverses ne font pas.
/// Allumé, le curseur est une perle dans le halo, comme les pastilles du site.
///
/// Toute la rangée est cliquable : viser une case de quinze pixels n'a jamais
/// été le but de personne.
/// </remarks>
internal static class Toggle
{
    /// <summary>La rangée complète. Rend vrai si l'état vient de changer.</summary>
    /// <param name="hint">Explication en infobulle, sur une icône ⓘ après le libellé.</param>
    /// <param name="hintContent">Comme <paramref name="hint"/>, mais dessinée librement.</param>
    public static bool Draw(string label, ref bool value, string id, string? hint = null, Action? hintContent = null)
    {
        using var scope = ImRaii.PushId(id);

        var start  = ImGui.GetCursorScreenPos();
        var width  = Math.Max(1f, ImGui.GetContentRegionAvail().X - Card.RightInset);
        var height = ImGui.GetFrameHeight();

        var clicked = ImGui.InvisibleButton("##row", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var dl    = ImGui.GetWindowDrawList();
        var track = TrackSize();
        var mid   = start.Y + height * 0.5f;

        var trackMin = new Vector2(start.X + width - track.X, mid - track.Y * 0.5f);

        // Le libellé s'arrête avant la piste : dans une fenêtre étroite, il
        // passerait dessous et l'interrupteur ne se lirait plus.
        var text     = Glyphs.Safe(label);
        var textSize = ImGui.CalcTextSize(text);
        var textEnd  = trackMin.X - Theme.S(Theme.GapL);

        dl.PushClipRect(start, new Vector2(textEnd, start.Y + height), true);
        dl.AddText(new Vector2(start.X, mid - textSize.Y * 0.5f), ImGui.GetColorU32(Theme.Text), text);
        dl.PopClipRect();

        if (hint is not null || hintContent is not null)
            DrawHint(dl, new Vector2(Math.Min(start.X + textSize.X, textEnd) + Theme.S(Theme.GapS), mid),
                     hint, hintContent);

        DrawTrack(dl, trackMin, value, hovered);

        if (clicked)
            value = !value;

        return clicked;
    }

    /// <summary>L'interrupteur seul, en ligne, pour une rangée qui porte déjà ses propres éléments.</summary>
    public static bool Switch(ref bool value, string id, string? tooltip = null)
    {
        var track   = TrackSize();
        var height  = ImGui.GetFrameHeight();
        var start   = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##switch_{id}", new Vector2(track.X, height));
        var hovered = ImGui.IsItemHovered();

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (tooltip is not null)
            Feedback.TooltipOnHover(tooltip);

        DrawTrack(ImGui.GetWindowDrawList(), new Vector2(start.X, start.Y + (height - track.Y) * 0.5f), value, hovered);

        if (clicked)
            value = !value;

        return clicked;
    }

    private static Vector2 TrackSize() => Theme.S(34f, 19f);

    private static void DrawTrack(ImDrawListPtr dl, Vector2 min, bool on, bool hovered)
    {
        var size   = TrackSize();
        var max    = min + size;
        var radius = size.Y * 0.5f;
        var knob   = radius - Theme.S(3f);

        if (on)
        {
            Surface.Glow(dl, min, max, radius, Theme.Accent, spread: 4f);
            dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Accent, hovered ? 0.45f : 0.35f)), radius);
            dl.AddRect(min, max, ImGui.GetColorU32(Theme.Accent), radius, ImDrawFlags.None, 1f);
            Surface.Pearl(dl, new Vector2(max.X - radius, min.Y + radius), knob, glow: false);
            return;
        }

        dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Text, hovered ? 0.12f : 0.08f)), radius);
        dl.AddRect(min, max, ImGui.GetColorU32(Theme.BorderLight), radius, ImDrawFlags.None, 1f);
        dl.AddCircleFilled(new Vector2(min.X + radius, min.Y + radius), knob, ImGui.GetColorU32(Theme.TextFaint));
    }

    /// <summary>L'icône ⓘ, dessinée à la main : la rangée entière est déjà un bouton.</summary>
    private static void DrawHint(ImDrawListPtr dl, Vector2 at, string? hint, Action? hintContent)
    {
        var glyph = Icons.Info.S();
        var size  = ImGui.CalcTextSize(glyph);
        var min   = new Vector2(at.X, at.Y - size.Y * 0.5f);

        dl.AddText(min, ImGui.GetColorU32(Theme.TextFaint), glyph);

        if (ImGui.IsMouseHoveringRect(min, min + size) is false)
            return;

        if (hintContent is not null)
            Feedback.Tooltip(hintContent);
        else if (hint is not null)
            Feedback.Tooltip(hint);
    }
}
```

- [ ] **Étape 3 : la carte Visibilité**

Dans `SettingsPage.DrawVisibility`, remplacer depuis `var discoverable = configuration.Discoverable;` jusqu'à la fin de la méthode par :

```csharp
        var discoverable = configuration.Discoverable;

        if (Toggle.Draw("Me signaler aux autres joueurs", ref discoverable, "discoverable",
                        hint: "Sans cela, personne ne peut vous reconnaître ni vous adresser une demande. Avec, "
                            + "l'opérateur de chaque service peut savoir que votre personnage est en ligne : pour "
                            + "qu'un inconnu puisse vous reconnaître, il faut bien que quelque chose soit calculable "
                            + "à partir de votre nom."))
        {
            configuration.Discoverable = discoverable;
            configuration.Save();
        }

        // Exception voulue à la règle « tout en infobulle » : ce qui se paie
        // en vie privée se lit avant de cocher, pas au survol d'une icône.
        Text.Small("Le service sait alors que vous êtes en ligne.", Theme.TextFaint);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var glyphs = configuration.ShowNameplateGlyphs;

        if (Toggle.Draw("Glyphe à côté du nom", ref glyphs, "nameplate_glyphs", hintContent: NameplateLegend.Draw))
        {
            configuration.ShowNameplateGlyphs = glyphs;
            configuration.Save();
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var badges = configuration.ShowTransferBadges;

        if (Toggle.Draw("Badges de transfert", ref badges, "transfer_badges",
                        hint: "Un badge aux pieds d'un pair visible dont l'apparence n'est pas encore là : connexion, "
                            + "attente, réception avec sa progression, application. Il disparaît dès qu'elle est posée."))
        {
            configuration.ShowTransferBadges = badges;
            configuration.Save();
        }
    }
```

- [ ] **Étape 4 : la carte Réseau**

Dans `DrawNetwork`, remplacer la case « Brider l'envoi » et le `Feedback.Hint(...)` qui la suit par :

```csharp
        var limited = configuration.LimitUpload;

        if (Toggle.Draw("Brider l'envoi", ref limited, "limit_upload",
                        hint: "Bridé, l'envoi démarre lentement et recule dès que le ping gonfle : une tenue met des "
                            + "minutes à arriver, mais le jeu reste fluide en donjon. Libre, elle arrive en quelques "
                            + "secondes, au prix d'un ping plus haut pendant le transfert."))
            setUploadLimited(limited);
```

Le `ImGui.SameLine();` qui suit le champ d'adresse `##nouveau` devient `ImGui.SameLine(0f, Theme.S(Theme.GapS));`.

- [ ] **Étape 5 : un service, et un service proposé**

Dans `DrawService`, remplacer la case `##actif`, son bloc et le `ImGui.SameLine();` qui la suit par :

```csharp
        if (Toggle.Switch(ref enabled, "actif",
                          enabled ? "Service actif. Cliquer pour le couper." : "Service coupé. Cliquer pour l'activer."))
        {
            configuration.Rendezvous[index] = entry with { Enabled = enabled };
            configuration.Save();
        }

        ImGui.SameLine(0f, Theme.S(Theme.GapM));
```

Les deux autres `ImGui.SameLine();` de `DrawService` deviennent `ImGui.SameLine(0f, Theme.S(Theme.GapS));`, et la largeur réservée aux deux boutons suit l'espacement désormais explicite :

```csharp
        var buttons = ImGui.GetFrameHeight() * 2f + Theme.S(Theme.GapS);
```

Dans `DrawDiscovery`, remplacer la case `##choisi` et son bloc, puis le `ImGui.SameLine();` qui suit, par :

```csharp
            if (Toggle.Switch(ref chosen, "choisi"))
            {
                if (chosen)
                    discovery.Chosen.Add(offered.Address);
                else
                    discovery.Chosen.Remove(offered.Address);
            }

            ImGui.SameLine(0f, Theme.S(Theme.GapM));
```

Les autres `ImGui.SameLine();` de `DrawDiscovery` deviennent `ImGui.SameLine(0f, Theme.S(Theme.GapS));`.

- [ ] **Étape 6 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests` (attendu : PASS)

- [ ] **Étape 7 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu, page Réglages :
- les quatre interrupteurs alignés au bord droit, la perle quand ils sont allumés ;
- toute la rangée réagit au clic ; l'icône ⓘ montre son infobulle, et la légende des glyphes pour le deuxième ;
- un service coupé puis réactivé par son interrupteur, l'adresse grisée quand il est coupé ;
- **fenêtre réduite au minimum** (point de vigilance 2) : « Me signaler aux autres joueurs » rogné avant l'interrupteur, jamais dessous ;
- à 150 % : pistes et perles à l'échelle.

- [ ] **Étape 8 : commit**

```bash
git add Linkpearl/Ui/Components/Toggle.cs Linkpearl/Ui/Pages/SettingsPage.cs Linkpearl.Core.Tests/UiConventionTests.cs
git commit -m "feat(ui): Réglages en interrupteurs, curseur en perle"
```

---

### Tâche 11 : les fenêtres annexes, et la fin des listes d'attente

**Fichiers :**
- Modifier : `Linkpearl/Ui/Onboarding/OnboardingArt.cs` (couleur de « votre ami »)
- Modifier : `Linkpearl/Ui/Onboarding/OnboardingWindow.cs` (`DrawNavigation`, points de progression)
- Modifier : `Linkpearl/Ui/NameplateGlyphs.cs` (`ColorOf`)
- Modifier : `Linkpearl/Ui/NameplateLegend.cs` (`Line`)
- Modifier : `Linkpearl/Ui/TransferOverlay.cs` (`DrawBadge`)
- Modifier : `Linkpearl.Core.Tests/UiConventionTests.cs`
- Modifier : `docs/reprise.md`

- [ ] **Étape 1 : les règles qui échouent**

Retirer `"NameplateGlyphs.cs"` et `"Onboarding.OnboardingArt.cs"` des fichiers en attente de `Hex(0x`, et `"NameplateLegend.cs"` de ceux de `ImGui.SameLine()`. Toutes les listes en attente sont alors vides.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter UiConventionTests`
Attendu : FAIL sur `Hex(0x` et `ImGui.SameLine()`.

- [ ] **Étape 2 : les glyphes des noms**

`NameplateGlyphs.ColorOf` et son commentaire deviennent :

```csharp
    /// <summary>La couleur d'une marque, partagée avec la légende des réglages.</summary>
    /// <remarks>
    /// Les couleurs vives de la palette : un glyphe de quelques pixels posé sur
    /// la scène se lit mal en pastel. L'orange pompon pour qui attend qu'on
    /// l'aborde, le halo pour une demande, la lavande de la perle pour un groupe.
    /// </remarks>
    public static Vector4 ColorOf(NameplateMark mark) => mark switch
    {
        NameplateMark.Online      => Theme.Online,
        NameplateMark.Available   => Theme.Action,
        NameplateMark.Requesting  => Theme.Accent,
        NameplateMark.Offline     => Theme.TextFaint,
        NameplateMark.Trouble     => Theme.Danger,
        NameplateMark.GroupMember => Theme.PearlRim,
        _                         => Theme.Text,
    };
```

Dans `NameplateLegend.Line`, `ImGui.SameLine();` devient `ImGui.SameLine(0f, Theme.S(Theme.GapS));`.

- [ ] **Étape 3 : l'onboarding**

Dans `OnboardingArt.cs`, `Person(dl, right, Theme.Hex(0x6FB6F2), "votre ami");` devient :

```csharp
        Person(dl, right, Theme.Accent, "votre ami");
```

Dans `OnboardingWindow.DrawNavigation`, la boucle des points de progression (le commentaire `// Les points de progression.` reste au-dessus des variables) devient :

```csharp
        // L'étape courante en perle, les autres en points éteints : la même
        // pastille que la barre d'état, pour que le plugin parle une seule langue.
        for (var i = 0; i < _steps.Length; i++)
        {
            var center = origin + new Vector2(radius + spacing * i, Theme.S(14f));

            if (i == _step)
                Surface.Pearl(dl, center, radius * 1.25f, glow: false);
            else
                dl.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.Alpha(Theme.TextFaint, 0.5f)));
        }
```

- [ ] **Étape 4 : les badges de transfert**

Dans `TransferOverlay.DrawBadge`, la ligne `dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.BgSurface, 0.88f)), rounding);` devient :

```csharp
        // La nuit, presque opaque : le badge flotte sur n'importe quel décor du
        // jeu, et en deçà de 85 % le texte se perd dans une scène claire.
        dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.BgBase, 0.90f)), rounding);
```

Le reste du badge ne change pas : sa bordure et sa barre sont déjà en `Theme.Accent`.

- [ ] **Étape 5 : compiler et tester**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release` (attendu : 0 warning, 0 erreur)
Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj` (attendu : PASS, toute la suite)

Vérifier que les listes d'attente sont vides :

Run : `grep -n '\["' Linkpearl.Core.Tests/UiConventionTests.cs`
Attendu : seules les colonnes d'exemptions, `["Components.Btn.cs"]` et `["Theme.cs"]`.

- [ ] **Étape 6 : déployer et regarder**

Run : `./scripts/deploy-plugin-dev.sh`
En jeu :
- Réglages, Identité, « Revoir la présentation » : la nuit sous le titre, la perle à l'étape courante, « Suivant » en orange ;
- les glyphes des noms : orange pour un joueur disponible, lavande pour un membre de groupe ; la légende des réglages dans les mêmes couleurs ;
- un transfert en cours : badge marine lisible sur un décor clair.

- [ ] **Étape 7 : la reprise**

Dans `docs/reprise.md`, après la section « Ce qui a changé le 24 », ajouter :

```markdown
## Ce qui a changé le 25

- **L'interface a pris l'apparence du site et du logo** : nuit marine, halo
  bleu, orange pompon réservé à l'action principale, perle, Fredoka pour les
  titres et Nunito pour le texte. Voir
  `superpowers/specs/2026-09-25-design-nuit-design.md`.
- Inter reste embarqué, fusionné derrière les deux polices, pour les glyphes
  qu'elles n'ont pas. `scripts/generer-polices.sh` régénère les fichiers et
  échoue si un caractère de l'interface n'a plus de police.
- `UiConventionTests` refuse, sous Linux, ce qui contournerait les composants :
  case ImGui brute, en-tête repliable natif, `SameLine()` sans espacement,
  bouton hors de `Btn`, couleur en dur, `BtnTone.Primary`.
```

Et dans « Pièges appris à la dure », ajouter :

```markdown
- Les bindings ImGui de Dalamud n'ont ni dégradé radial ni ellipse : le halo
  est un éventail de triangles colorés par sommet (`Surface.Halo`), que la carte
  graphique interpole sans marches.
- La liste de dessin d'une fenêtre est rognée en deçà des marges : un fond peint
  depuis `Draw` doit pousser son propre rectangle de rognage, sans intersection.
```

- [ ] **Étape 8 : commit**

```bash
git add Linkpearl/Ui Linkpearl.Core.Tests/UiConventionTests.cs docs/reprise.md
git commit -m "feat(ui): fenêtres annexes et glyphes aux couleurs du site"
```
