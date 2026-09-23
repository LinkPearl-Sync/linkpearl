# Intégrations Customize+, SimpleHeels, Honorific, Moodles, PetNicknames : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Synchroniser les cinq intégrations des clients comparables dans un manifeste v2, nettoyées à l'envoi, validées à la réception, appliquées sans redessin quand seules elles changent.

**Architecture:** Le noyau (`Linkpearl/Core`, sans Dalamud, testé sous Linux) porte le modèle `CharacterExtras`, le codec, les nettoyeurs, le codec MemoryPack de Moodles, le validateur et la comparaison `ExtrasDiff`. L'adaptateur (`Linkpearl/Integration`) porte les cinq appels IPC, la lecture locale et l'application. Le moteur choisit entre application complète et application des seuls extras.

**Tech Stack:** C# / .NET 10, Dalamud (IPC par `GetIpcSubscriber`), xUnit, System.Text.Json, MemoryPack 1.21.4 (projet de tests uniquement).

**Spec:** `docs/superpowers/specs/2026-09-23-integrations-design.md`

## Global Constraints

- `Linkpearl/Core/` ne référence jamais Dalamud ni MemoryPack ; `ArchitectureTests` le vérifie.
- Toute donnée venant d'un pair passe par `Core/Safety` ; un seul champ fautif rejette le manifeste entier.
- Le noyau ne voit jamais un nom de personnage ni un ContentId ; aucun des deux n'est journalisé.
- IPC, `ObjectTable` et interface uniquement sur le thread du framework ; retour par `Framework.RunOnFrameworkThread`.
- Ordre d'application : collection, mod temporaire, redessin, Glamourer verrouillé, **puis** extras.
- Pas de tiret cadratin (le caractère U+2014) nulle part ; commentaires en français qui expliquent le pourquoi.
- Commits en Conventional Commits, sujet en français, **sans** `Co-Authored-By`, `Claude-Session` ni « Generated with ».
- Versions exigées : Customize+ majeure 6, SimpleHeels majeure 2, Honorific majeure 3, Moodles exactement 4, PetRenamer majeure 4 et `IsEnabled`.
- Plafonds : CustomizePlus 64 Kio, Heels 16 Kio, Honorific 4 Kio, Moodles 32 Kio, PetNicknames 16 Kio (en caractères de la chaîne transportée).
- `dotnet build Linkpearl/Linkpearl.csproj -c Release` doit passer sans warning ; après chaque tâche du plugin, `./scripts/deploy-plugin-dev.sh`.

## Review Focus

- Un plugin qui répond mais lève une exception (déchargé en cours de route) : l'extra est ignoré, l'apparence reste posée. Couvert par `ExtrasIpc.Put` et `Read` (tâche 8) ; vérifié en jeu (tâche 10, essai 7).
- Un Moodles au format d'une version future (plus de 14 membres) : champ omis à l'envoi, jamais d'exception. Test en tâche 4.
- Un PetNicknames en fins de ligne `\n` au lieu de `\r\n` : même traitement. Test en tâche 3.
- Un manifeste v1 d'un ancien client, sans extras : accepté, rien n'est retiré à tort. Test en tâche 1.
- Des fichiers qui changent en même temps que les extras : application complète, jamais les seuls extras. Test en tâche 6.

---

### Task 1: Modèle `CharacterExtras` et manifeste v2

**Files:**
- Modify: `Linkpearl/Core/Manifest/CharacterManifest.cs`
- Modify: `Linkpearl/Core/Manifest/ManifestCodec.cs`
- Modify: `Linkpearl/Core/Safety/ManifestValidator.cs:17-21`
- Test: `Linkpearl.Core.Tests/Manifest/ManifestExtrasTests.cs` (créer)

**Interfaces:**
- Produces: `public sealed record CharacterExtras(string? CustomizePlus, string? Heels, string? Honorific, string? Moodles, string? PetNicknames)` avec `public static CharacterExtras None { get; }` et `public bool IsEmpty`. `CharacterManifest` gagne un cinquième paramètre positionnel `CharacterExtras? Extras = null` et une propriété `public CharacterExtras ExtrasOrNone => Extras ?? CharacterExtras.None;`. `CharacterManifest.CurrentVersion == 2`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class ManifestExtrasTests
{
    private static CharacterManifest With(CharacterExtras? extras)
        => new(CharacterManifest.CurrentVersion,
               [new FileReplacement(["chara/equipment/e0001/model/c0101e0001_top.mdl"], BlobHash.OfContent("x"u8), 1)],
               "", null, extras);

    [Fact]
    public void Les_extras_font_l_aller_retour()
    {
        var extras = new CharacterExtras("{\"Bones\":{}}", "{\"DefaultOffset\":0.1}", "{\"Title\":\"x\"}", "AAAAAA==", "QQA=");

        var bytes = ManifestCodec.Compress(With(extras));

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.Equal(extras, back!.ExtrasOrNone);
    }

    [Fact]
    public void Un_manifeste_sans_extras_se_relit_sans_extras()
    {
        var bytes = ManifestCodec.Compress(With(null));

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.True(back!.ExtrasOrNone.IsEmpty);
    }

    [Fact]
    public void Un_manifeste_v1_d_un_ancien_client_est_accepte()
    {
        var v1 = With(null) with { Version = 1 };
        var bytes = ManifestCodec.Compress(v1);

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.True(ManifestValidator.TryAccept(back!, Quotas.Default, out var refus), refus);
        Assert.True(back!.ExtrasOrNone.IsEmpty);
    }

    [Fact]
    public void Un_titre_qui_change_change_l_empreinte_du_manifeste()
    {
        var before = With(new CharacterExtras(null, null, "{\"Title\":\"a\"}", null, null));
        var after = With(new CharacterExtras(null, null, "{\"Title\":\"b\"}", null, null));

        Assert.NotEqual(ManifestCodec.HashOf(before), ManifestCodec.HashOf(after));
    }

    [Fact]
    public void Des_extras_vides_ne_changent_pas_l_empreinte()
    {
        Assert.Equal(ManifestCodec.HashOf(With(null)), ManifestCodec.HashOf(With(CharacterExtras.None)));
    }

    [Fact]
    public void Un_extra_non_textuel_est_refuse()
    {
        var json = "{\"v\":2,\"r\":[],\"m\":\"\",\"g\":null,\"xt\":42}"u8.ToArray();
        using var output = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            brotli.Write(json);

        Assert.False(ManifestCodec.TryDecompress(output.ToArray(), Quotas.Default, out _, out _));
    }
}
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ManifestExtrasTests`
Expected: FAIL à la compilation, `CharacterExtras` introuvable.

- [ ] **Step 3: Écrire le modèle**

Dans `CharacterManifest.cs`, remplacer la déclaration du record par :

```csharp
public sealed record CharacterManifest(
    ushort Version,
    IReadOnlyList<FileReplacement> Replacements,
    string MetaManipulations,
    string? GlamourerState,
    CharacterExtras? Extras = null)
{
    /// <summary>
    /// 2 depuis les intégrations : un manifeste peut porter des extras. Un
    /// manifeste 1 reste lisible, sans extras.
    /// </summary>
    public const ushort CurrentVersion = 2;

    public CharacterExtras ExtrasOrNone => Extras ?? CharacterExtras.None;
}

/// <summary>
/// Ce que les plugins voisins montrent du personnage, au format natif de chacun.
/// </summary>
/// <remarks>
/// Null veut dire « pas de ce plugin, ou rien à montrer ». Les chaînes sont
/// opaques pour le noyau, qui n'en vérifie que la forme et la taille
/// (<see cref="Safety.ExtrasValidator"/>) : ce sont les plugins qui leur donnent
/// un sens. Moodles et PetNicknames arrivent déjà nettoyés de tout identifiant.
/// </remarks>
public sealed record CharacterExtras(
    string? CustomizePlus,
    string? Heels,
    string? Honorific,
    string? Moodles,
    string? PetNicknames)
{
    public static CharacterExtras None { get; } = new(null, null, null, null, null);

    public bool IsEmpty => this == None;
}
```

- [ ] **Step 4: Écrire le codec**

Dans `ManifestCodec.Encode`, juste avant le `writer.WriteEndObject();` final, donc après l'écriture de `g` :

```csharp
            // Une clé par extra présent, et aucune pour les absents : un
            // manifeste sans extras garde ainsi exactement l'encodage, donc
            // l'empreinte, qu'il avait avant les intégrations.
            var extras = manifest.ExtrasOrNone;
            WriteExtra(writer, "xc", extras.CustomizePlus);
            WriteExtra(writer, "xh", extras.Heels);
            WriteExtra(writer, "xt", extras.Honorific);
            WriteExtra(writer, "xm", extras.Moodles);
            WriteExtra(writer, "xp", extras.PetNicknames);
```

Et dans la classe :

```csharp
    private static void WriteExtra(Utf8JsonWriter writer, string key, string? value)
    {
        if (value is not null)
            writer.WriteString(key, value);
    }

    private static string? ReadExtra(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var element) is false)
            return null;

        return element.ValueKind is JsonValueKind.String
            ? element.GetString()
            : throw new JsonException($"extra {key} non textuel");
    }
```

Dans `TryParse`, remplacer la construction finale `manifest = new CharacterManifest(version, replacements, metaElement.GetString()!, glamourer);` par :

```csharp
            var extras = new CharacterExtras(
                ReadExtra(root, "xc"), ReadExtra(root, "xh"), ReadExtra(root, "xt"),
                ReadExtra(root, "xm"), ReadExtra(root, "xp"));

            manifest = new CharacterManifest(
                version, replacements, metaElement.GetString()!, glamourer,
                extras.IsEmpty ? null : extras);
```

- [ ] **Step 5: Accepter les versions 1 et 2**

Dans `ManifestValidator.TryAccept`, remplacer le contrôle de version par :

```csharp
        // 1 : manifeste d'avant les intégrations, toujours lisible, sans extras.
        if (manifest.Version is not (1 or CharacterManifest.CurrentVersion))
        {
            rejection = $"version de manifeste inconnue ({manifest.Version}, attendu 1 ou {CharacterManifest.CurrentVersion})";
            return false;
        }

        if (manifest.Version == 1 && manifest.ExtrasOrNone.IsEmpty is false)
        {
            rejection = "manifeste de version 1 portant des extras";
            return false;
        }
```

- [ ] **Step 6: Lancer toute la suite**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS. Un test existant qui construisait un manifeste avec la version 1 en dur reste valide grâce à l'étape 5.

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Core/Manifest/CharacterManifest.cs Linkpearl/Core/Manifest/ManifestCodec.cs Linkpearl/Core/Safety/ManifestValidator.cs Linkpearl.Core.Tests/Manifest/ManifestExtrasTests.cs
git commit -m "feat(core): un manifeste v2 qui porte les extras des plugins voisins"
```

---

### Task 2: Forme JSON et nettoyage SimpleHeels

**Files:**
- Create: `Linkpearl/Core/Safety/JsonShape.cs`
- Create: `Linkpearl/Core/Manifest/HeelsSanitizer.cs`
- Test: `Linkpearl.Core.Tests/Manifest/HeelsSanitizerTests.cs`

**Interfaces:**
- Produces: `JsonShape.IsObject(string json, int maxDepth) : bool` (jamais d'exception) ; `HeelsSanitizer.Sanitize(string json) : string?` (null si illisible) ; `HeelsSanitizer.Removed : IReadOnlyList<string>` = `["EmotePosition", "MinionPosition", "Tags", "E", "PluginVersion"]`.

- [ ] **Step 1: Tests qui échouent**

```csharp
using System.Text.Json;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class HeelsSanitizerTests
{
    private const string Full =
        "{\"DefaultOffset\":0.035,\"EmotePosition\":{\"X\":101.5,\"Y\":2,\"Z\":-33,\"R\":0,\"Pitch\":0,\"Roll\":0},"
      + "\"MinionPosition\":{\"X\":1,\"Y\":2,\"Z\":3,\"R\":0,\"Pitch\":0,\"Roll\":0},\"Tags\":{\"autre\":\"secret\"},"
      + "\"E\":true,\"PluginVersion\":\"1.2.3.4\",\"Version\":2}";

    [Fact]
    public void Les_champs_qui_revelent_quelque_chose_disparaissent()
    {
        var clean = HeelsSanitizer.Sanitize(Full)!;
        using var doc = JsonDocument.Parse(clean);

        foreach (var removed in HeelsSanitizer.Removed)
            Assert.False(doc.RootElement.TryGetProperty(removed, out _), removed);
    }

    [Fact]
    public void Le_decalage_reste()
    {
        using var doc = JsonDocument.Parse(HeelsSanitizer.Sanitize(Full)!);

        Assert.Equal(0.035, doc.RootElement.GetProperty("DefaultOffset").GetDouble(), 6);
        Assert.Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("[1,2]")]
    public void Une_donnee_illisible_donne_null(string input)
        => Assert.Null(HeelsSanitizer.Sanitize(input));

    [Fact]
    public void Un_json_trop_profond_n_est_pas_un_objet_acceptable()
        => Assert.False(JsonShape.IsObject("{\"a\":{\"b\":{\"c\":{}}}}", maxDepth: 2));

    [Fact]
    public void Un_objet_simple_est_acceptable()
        => Assert.True(JsonShape.IsObject("{\"a\":1}", maxDepth: 8));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter HeelsSanitizerTests`
Expected: FAIL, types introuvables.

- [ ] **Step 3: Implémenter**

`Linkpearl/Core/Safety/JsonShape.cs` :

```csharp
using System.Text;
using System.Text.Json;

namespace Linkpearl.Core.Safety;

/// <summary>Vérifie qu'une chaîne venue d'un pair est un objet JSON de profondeur bornée.</summary>
/// <remarks>
/// La profondeur est bornée par le lecteur lui-même : un JSON imbriqué sur
/// des milliers de niveaux épuiserait la pile d'un parseur récursif, chez
/// nous ou chez le plugin à qui on le passe.
/// </remarks>
public static class JsonShape
{
    public static bool IsObject(string json, int maxDepth)
    {
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { MaxDepth = maxDepth });

            if (reader.Read() is false || reader.TokenType is not JsonTokenType.StartObject)
                return false;

            reader.Skip();
            return reader.Read() is false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

`Linkpearl/Core/Manifest/HeelsSanitizer.cs` :

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Retire de la configuration SimpleHeels ce qui ne sert pas au décalage et
/// en dit trop.
/// </summary>
/// <remarks>
/// Relevé dans SimpleHeels 162466c (IpcCharacterConfig) : les positions d'emote
/// et de familier sont des coordonnées absolues dans le monde, les étiquettes
/// sont des chaînes libres écrites par d'autres plugins, <c>E</c> révèle que le
/// plugin Echo est installé. Aucun n'est nécessaire au décalage.
/// </remarks>
public static class HeelsSanitizer
{
    public static IReadOnlyList<string> Removed { get; } =
        ["EmotePosition", "MinionPosition", "Tags", "E", "PluginVersion"];

    private const int MaxDepth = 8;

    public static string? Sanitize(string json)
    {
        if (JsonShape.IsObject(json, MaxDepth) is false)
            return null;

        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
                return null;

            foreach (var name in Removed)
                root.Remove(name);

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: Vérifier que ça passe**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter HeelsSanitizerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Safety/JsonShape.cs Linkpearl/Core/Manifest/HeelsSanitizer.cs Linkpearl.Core.Tests/Manifest/HeelsSanitizerTests.cs
git commit -m "feat(core): retirer de SimpleHeels les positions et étiquettes qui en disent trop"
```

---

### Task 3: PetNicknames sans identité

**Files:**
- Create: `Linkpearl/Core/Manifest/PetNicknamesData.cs`
- Test: `Linkpearl.Core.Tests/Manifest/PetNicknamesDataTests.cs`

**Interfaces:**
- Produces: `PetNicknamesData.Neutralize(string base64) : string?` ; `PetNicknamesData.IsNeutralAndBounded(string base64) : bool` ; constantes `Header = "[PetNicknames(4)]"`, `NeutralName = "Linkpearl"`, `NeutralWorld = "0"`, `NeutralContentId = "0"`, `MaxLines = 64`, `MaxLineLength = 256` ; `PetNicknamesData.TrySplit(string base64, out string[] lines, out string separator) : bool` et `PetNicknamesData.Join(string[] lines, string separator) : string`. `TrySplit` et `Join` servent aussi à l'adaptateur (tâche 7) pour réinjecter l'identité, hors du noyau.

- [ ] **Step 1: Tests qui échouent**

```csharp
using System.Text;
using Linkpearl.Core.Manifest;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class PetNicknamesDataTests
{
    private static string Encode(string separator, params string[] lines)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(string.Join(separator, lines)));

    private static readonly string[] Real =
    [
        "[PetNicknames(4)]", "Prénom Nom", "73", "18014398509481984",
        "[411^2,412^2]", "411^2^Sparky^<1, 0.5, 0>^null",
    ];

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Nom_monde_et_contentid_sont_neutralises(string separator)
    {
        var clean = PetNicknamesData.Neutralize(Encode(separator, Real))!;
        var text = Encoding.Unicode.GetString(Convert.FromBase64String(clean));

        Assert.DoesNotContain("Prénom", text);
        Assert.DoesNotContain("18014398509481984", text);
        Assert.Contains("Sparky", text);
        Assert.True(PetNicknamesData.IsNeutralAndBounded(clean));
    }

    [Fact]
    public void Des_donnees_non_neutralisees_sont_refusees()
        => Assert.False(PetNicknamesData.IsNeutralAndBounded(Encode("\r\n", Real)));

    [Theory]
    [InlineData("pas du base64 !")]
    [InlineData("")]
    public void Une_donnee_illisible_donne_null_sans_exception(string input)
    {
        Assert.Null(PetNicknamesData.Neutralize(input));
        Assert.False(PetNicknamesData.IsNeutralAndBounded(input));
    }

    [Fact]
    public void Un_autre_en_tete_est_refuse()
        => Assert.Null(PetNicknamesData.Neutralize(Encode("\r\n", "[PetNicknames(3)]", "a", "1", "2")));

    [Fact]
    public void Trop_de_lignes_sont_refusees()
    {
        var lines = new[] { PetNicknamesData.Header, "a", "1", "2" }
            .Concat(Enumerable.Repeat("411^2^x^null^null", PetNicknamesData.MaxLines)).ToArray();

        Assert.Null(PetNicknamesData.Neutralize(Encode("\r\n", lines)));
    }

    [Fact]
    public void Une_ligne_trop_longue_est_refusee()
        => Assert.Null(PetNicknamesData.Neutralize(
            Encode("\r\n", PetNicknamesData.Header, "a", "1", "2", new string('x', PetNicknamesData.MaxLineLength + 1))));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter PetNicknamesDataTests`
Expected: FAIL, type introuvable.

- [ ] **Step 3: Implémenter**

```csharp
using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Les données de PetNicknames, privées de ce qui désigne le joueur.
/// </summary>
/// <remarks>
/// Relevé dans FFXIVPetRenamer 7192264 (WriterElementVersion4) : base64 d'un
/// texte UTF-16 en lignes, dont les lignes 1 à 3 sont le nom du personnage,
/// son monde d'origine et son ContentId. Le projet s'interdit de faire sortir
/// ce dernier : même le dossier local du personnage n'en porte qu'une
/// empreinte. L'émetteur les remplace par des valeurs neutres, et l'adaptateur
/// du receveur y remet celles du personnage qu'il voit, lues chez lui.
/// </remarks>
public static class PetNicknamesData
{
    public const string Header = "[PetNicknames(4)]";
    public const string NeutralName = "Linkpearl";
    public const string NeutralWorld = "0";
    public const string NeutralContentId = "0";
    public const int MaxLines = 64;
    public const int MaxLineLength = 256;

    public static string? Neutralize(string base64)
    {
        if (TrySplit(base64, out var lines, out var separator) is false)
            return null;

        lines[1] = NeutralName;
        lines[2] = NeutralWorld;
        lines[3] = NeutralContentId;

        return Join(lines, separator);
    }

    public static bool IsNeutralAndBounded(string base64)
        => TrySplit(base64, out var lines, out _)
        && lines[1] == NeutralName
        && lines[2] == NeutralWorld
        && lines[3] == NeutralContentId;

    /// <summary>Découpe en lignes, dans les bornes, en-tête vérifié. Jamais d'exception.</summary>
    public static bool TrySplit(string base64, out string[] lines, out string separator)
    {
        lines = [];
        separator = "\r\n";

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length == 0 || bytes.Length % 2 != 0)
            return false;

        var text = Encoding.Unicode.GetString(bytes);
        separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var split = text.Split(separator);

        if (split.Length < 4 || split.Length > MaxLines || split[0] != Header)
            return false;

        if (split.Any(line => line.Length > MaxLineLength))
            return false;

        lines = split;
        return true;
    }

    public static string Join(string[] lines, string separator)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(string.Join(separator, lines)));
}
```

- [ ] **Step 4: Vérifier que ça passe**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter PetNicknamesDataTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Core/Manifest/PetNicknamesData.cs Linkpearl.Core.Tests/Manifest/PetNicknamesDataTests.cs
git commit -m "feat(core): PetNicknames sans nom, monde ni ContentId"
```

---

### Task 4: Codec et nettoyage Moodles

**Files:**
- Create: `Linkpearl/Core/Manifest/MoodlesCodec.cs`
- Create: `Linkpearl/Core/Manifest/MoodlesSanitizer.cs`
- Modify: `Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
- Test: `Linkpearl.Core.Tests/Manifest/MoodlesCodecTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record MoodleStatus(Guid Id, int IconId, string Title, string Description, string CustomFxPath, long ExpiresAt, int Type, uint Modifiers, int Stacks, int StackSteps, Guid ChainedStatus, int ChainTrigger, string Applier, string Dispeller)`
  - `MoodlesCodec.TryDecode(ReadOnlySpan<byte> data, out IReadOnlyList<MoodleStatus> statuses) : bool` ; `MoodlesCodec.Encode(IReadOnlyList<MoodleStatus>) : byte[]` ; constantes `MaxStatuses = 64`, `MaxStringLength = 1024`, `MemberCount = 14`.
  - `MoodlesSanitizer.KeyFor(ReadOnlySpan<byte> identitySecret) : byte[]` ; `MoodlesSanitizer.Sanitize(string base64, ReadOnlySpan<byte> key) : string?` ; `MoodlesSanitizer.IsSanitizedAndBounded(string base64) : bool`.

Format (MemoryPack 1.21.4, `StringEncoding.Utf16`, relevé dans Moodles f4b7578, `Data/MyStatus.cs` et `MyStatusManager.cs:9-12, 249`) : liste = `int32` nombre (−1 si nulle) ; objet = 1 octet de nombre de membres (255 si nul) ; puis dans l'ordre `Guid`(16) `int` `string` `string` `string` `long` `int` `uint` `int` `int` `Guid`(16) `int` `string` `string` ; chaîne = `int32` longueur en caractères (−1 si nulle) puis `2 × longueur` octets UTF-16LE. Entiers en little-endian ; `Guid` dans sa représentation mémoire (celle de `Guid.ToByteArray()`). `StatusType` et `ChainTrigger` sont des `int`, `Modifiers` un `uint`.

- [ ] **Step 1: Ajouter MemoryPack au projet de tests**

Dans `Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`, dans l'`ItemGroup` qui contient `xunit` :

```xml
    <!-- Pour vérifier le codec Moodles contre la vraie bibliothèque. Le noyau
         n'en dépend pas : il décode ce format à la main, dans ses bornes. -->
    <PackageReference Include="MemoryPack" Version="1.21.4" />
```

Run: `dotnet restore Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: restauration réussie.

- [ ] **Step 2: Tests qui échouent**

```csharp
using System.Security.Cryptography;
using Linkpearl.Core.Manifest;
using MemoryPack;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

// Réplique exacte de Moodles.Data.MyStatus (f4b7578) : mêmes membres, même ordre, mêmes types.
public enum TestStatusType { Positive, Negative, Special }

public enum TestChainTrigger { Dispel, HitSomething }

[MemoryPackable]
public partial class TestMyStatus
{
    public Guid GUID = Guid.NewGuid();
    public int IconID;
    public string Title = "";
    public string Description = "";
    public string CustomFXPath = "";
    public long ExpiresAt;
    public TestStatusType Type;
    public uint Modifiers;
    public int Stacks = 1;
    public int StackSteps = 0;
    public Guid ChainedStatus = Guid.Empty;
    public TestChainTrigger ChainTrigger;
    public string Applier = "";
    public string Dispeller = "";
}

public class MoodlesCodecTests
{
    private static readonly MemoryPackSerializerOptions Options = new() { StringEncoding = StringEncoding.Utf16 };

    private static List<TestMyStatus> Sample() =>
    [
        new()
        {
            IconID = 210456, Title = "Fatigué", Description = "Une longue nuit", CustomFXPath = "vfx/common/eff/x.avfx",
            ExpiresAt = 1_790_000_000_000, Type = TestStatusType.Negative, Modifiers = 3, Stacks = 2, StackSteps = 1,
            ChainedStatus = Guid.NewGuid(), ChainTrigger = TestChainTrigger.HitSomething,
            Applier = "Prénom Nom@Monde", Dispeller = "Autre Nom@Monde",
        },
        new() { IconID = 1, Title = "", Description = "" },
    ];

    [Fact]
    public void Decode_puis_encode_redonne_les_memes_octets_que_MemoryPack()
    {
        var original = MemoryPackSerializer.Serialize(Sample(), Options);

        Assert.True(MoodlesCodec.TryDecode(original, out var statuses));
        Assert.Equal(original, MoodlesCodec.Encode(statuses));
    }

    [Fact]
    public void Ce_que_nous_encodons_se_relit_par_MemoryPack()
    {
        Assert.True(MoodlesCodec.TryDecode(MemoryPackSerializer.Serialize(Sample(), Options), out var statuses));

        var back = MemoryPackSerializer.Deserialize<List<TestMyStatus>>(MoodlesCodec.Encode(statuses), Options)!;

        Assert.Equal("Fatigué", back[0].Title);
        Assert.Equal(2, back[0].Stacks);
    }

    [Fact]
    public void Des_donnees_tronquees_sont_refusees_sans_exception()
    {
        var bytes = MemoryPackSerializer.Serialize(Sample(), Options);

        for (var length = 0; length < bytes.Length; length++)
            Assert.False(MoodlesCodec.TryDecode(bytes.AsSpan(0, length), out _), $"longueur {length}");
    }

    [Fact]
    public void Un_objet_d_une_version_future_est_refuse()
    {
        var bytes = MemoryPackSerializer.Serialize(Sample(), Options);
        bytes[4] = MoodlesCodec.MemberCount + 1;   // en-tête du premier objet, juste après le nombre (int32)

        Assert.False(MoodlesCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Trop_de_statuts_sont_refuses()
    {
        var many = Enumerable.Range(0, MoodlesCodec.MaxStatuses + 1).Select(_ => new TestMyStatus()).ToList();

        Assert.False(MoodlesCodec.TryDecode(MemoryPackSerializer.Serialize(many, Options), out _));
    }

    [Fact]
    public void Le_nettoyage_vide_les_noms_et_le_vfx()
    {
        var key = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var raw = Convert.ToBase64String(MemoryPackSerializer.Serialize(Sample(), Options));

        var clean = MoodlesSanitizer.Sanitize(raw, key)!;
        var back = MemoryPackSerializer.Deserialize<List<TestMyStatus>>(Convert.FromBase64String(clean), Options)!;

        Assert.All(back, s => Assert.Equal("", s.Applier));
        Assert.All(back, s => Assert.Equal("", s.Dispeller));
        Assert.All(back, s => Assert.Equal("", s.CustomFXPath));
        Assert.True(MoodlesSanitizer.IsSanitizedAndBounded(clean));
        Assert.False(MoodlesSanitizer.IsSanitizedAndBounded(raw));
    }

    [Fact]
    public void Les_identifiants_sont_stables_pour_un_personnage_et_differents_d_un_autre()
    {
        var sample = Sample();
        var raw = Convert.ToBase64String(MemoryPackSerializer.Serialize(sample, Options));

        Guid FirstId(byte[] key)
            => MemoryPackSerializer.Deserialize<List<TestMyStatus>>(
                Convert.FromBase64String(MoodlesSanitizer.Sanitize(raw, key)!), Options)![0].GUID;

        var alice = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var bob = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(FirstId(alice), FirstId(alice));
        Assert.NotEqual(FirstId(alice), FirstId(bob));
        Assert.NotEqual(sample[0].GUID, FirstId(alice));
    }

    [Fact]
    public void Une_liste_vide_de_moodles_reste_valide()
    {
        var key = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var empty = Convert.ToBase64String(MemoryPackSerializer.Serialize(new List<TestMyStatus>(), Options));

        Assert.True(MoodlesSanitizer.IsSanitizedAndBounded(MoodlesSanitizer.Sanitize(empty, key)!));
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter MoodlesCodecTests`
Expected: FAIL, `MoodlesCodec` introuvable.

- [ ] **Step 4: Implémenter le codec**

`Linkpearl/Core/Manifest/MoodlesCodec.cs` :

```csharp
using System.Buffers.Binary;
using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>Un statut Moodles, membre pour membre.</summary>
public sealed record MoodleStatus(
    Guid Id, int IconId, string Title, string Description, string CustomFxPath, long ExpiresAt,
    int Type, uint Modifiers, int Stacks, int StackSteps, Guid ChainedStatus, int ChainTrigger,
    string Applier, string Dispeller);

/// <summary>
/// Le format binaire de Moodles, décodé à la main et dans ses bornes.
/// </summary>
/// <remarks>
/// Moodles sérialise <c>List&lt;MyStatus&gt;</c> par MemoryPack 1.21.4, chaînes
/// en UTF-16 (relevé dans Moodles f4b7578). Décodé ici plutôt que par la
/// bibliothèque : le noyau ne dépend de rien, et une entrée venue d'un pair se
/// lit octet par octet, sans réflexion ni allocation dictée par elle. Le test
/// d'aller-retour contre la vraie bibliothèque attrape toute dérive.
///
/// Un objet d'un autre nombre de membres est refusé : un Moodles futur qui
/// ajouterait un champ apporterait peut-être un nouvel identifiant.
/// </remarks>
public static class MoodlesCodec
{
    public const byte MemberCount = 14;
    public const int MaxStatuses = 64;
    public const int MaxStringLength = 1024;

    public static bool TryDecode(ReadOnlySpan<byte> data, out IReadOnlyList<MoodleStatus> statuses)
    {
        statuses = [];
        var reader = new Reader(data);

        if (reader.TryInt32(out var count) is false || count < 0 || count > MaxStatuses)
            return false;

        var list = new List<MoodleStatus>(count);

        for (var i = 0; i < count; i++)
        {
            if (reader.TryByte(out var members) is false || members != MemberCount)
                return false;

            if (reader.TryGuid(out var id) is false
                || reader.TryInt32(out var icon) is false
                || reader.TryString(out var title) is false
                || reader.TryString(out var description) is false
                || reader.TryString(out var fx) is false
                || reader.TryInt64(out var expires) is false
                || reader.TryInt32(out var type) is false
                || reader.TryUInt32(out var modifiers) is false
                || reader.TryInt32(out var stacks) is false
                || reader.TryInt32(out var steps) is false
                || reader.TryGuid(out var chained) is false
                || reader.TryInt32(out var trigger) is false
                || reader.TryString(out var applier) is false
                || reader.TryString(out var dispeller) is false)
                return false;

            list.Add(new MoodleStatus(id, icon, title, description, fx, expires, type, modifiers,
                                      stacks, steps, chained, trigger, applier, dispeller));
        }

        if (reader.AtEnd is false)
            return false;

        statuses = list;
        return true;
    }

    public static byte[] Encode(IReadOnlyList<MoodleStatus> statuses)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);

        writer.Write(statuses.Count);

        foreach (var s in statuses)
        {
            writer.Write(MemberCount);
            writer.Write(s.Id.ToByteArray());
            writer.Write(s.IconId);
            WriteString(writer, s.Title);
            WriteString(writer, s.Description);
            WriteString(writer, s.CustomFxPath);
            writer.Write(s.ExpiresAt);
            writer.Write(s.Type);
            writer.Write(s.Modifiers);
            writer.Write(s.Stacks);
            writer.Write(s.StackSteps);
            writer.Write(s.ChainedStatus.ToByteArray());
            writer.Write(s.ChainTrigger);
            WriteString(writer, s.Applier);
            WriteString(writer, s.Dispeller);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        writer.Write(value.Length);
        writer.Write(Encoding.Unicode.GetBytes(value));
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _rest = data;

        public readonly bool AtEnd => _rest.IsEmpty;

        public bool TryByte(out byte value)
        {
            value = 0;
            if (_rest.Length < 1) return false;
            value = _rest[0];
            _rest = _rest[1..];
            return true;
        }

        public bool TryInt32(out int value)
        {
            value = 0;
            if (_rest.Length < 4) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(_rest);
            _rest = _rest[4..];
            return true;
        }

        public bool TryUInt32(out uint value)
        {
            value = 0;
            if (_rest.Length < 4) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(_rest);
            _rest = _rest[4..];
            return true;
        }

        public bool TryInt64(out long value)
        {
            value = 0;
            if (_rest.Length < 8) return false;
            value = BinaryPrimitives.ReadInt64LittleEndian(_rest);
            _rest = _rest[8..];
            return true;
        }

        public bool TryGuid(out Guid value)
        {
            value = Guid.Empty;
            if (_rest.Length < 16) return false;
            value = new Guid(_rest[..16]);
            _rest = _rest[16..];
            return true;
        }

        /// <summary>Chaîne UTF-16 de MemoryPack. Une chaîne nulle (−1) est refusée : Moodles n'en écrit pas.</summary>
        public bool TryString(out string value)
        {
            value = "";
            if (TryInt32(out var length) is false || length < 0 || length > MaxStringLength)
                return false;

            if (_rest.Length < length * 2) return false;
            value = Encoding.Unicode.GetString(_rest[..(length * 2)]);
            _rest = _rest[(length * 2)..];
            return true;
        }
    }
}
```

- [ ] **Step 5: Implémenter le nettoyage**

`Linkpearl/Core/Manifest/MoodlesSanitizer.cs` :

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Linkpearl.Core.Manifest;

/// <summary>
/// Retire des Moodles ce qui désigne des personnes ou relie des personnages.
/// </summary>
/// <remarks>
/// <c>Applier</c> et <c>Dispeller</c> portent « Nom@Monde », parfois celui d'un
/// tiers. Le GUID d'un moodle enregistré est le même sur tous les personnages
/// d'une même personne : transmis tel quel, il permettrait de les relier, ce que
/// l'identité par personnage cherche précisément à empêcher. Il est remplacé
/// par un HMAC sous une clé propre au personnage, stable d'une mise à jour à
/// l'autre. <c>CustomFXPath</c> fait jouer un VFX : ressource transitoire, qui
/// relève du blocage du sous-projet B.
/// </remarks>
public static class MoodlesSanitizer
{
    private static readonly byte[] Info = Encoding.ASCII.GetBytes("linkpearl:moodles-guid:v1");

    public static byte[] KeyFor(ReadOnlySpan<byte> identitySecret)
        => HKDF.DeriveKey(HashAlgorithmName.SHA256, identitySecret.ToArray(), 32, salt: [], info: Info);

    public static string? Sanitize(string base64, ReadOnlySpan<byte> key)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        if (MoodlesCodec.TryDecode(raw, out var statuses) is false)
            return null;

        var clean = new List<MoodleStatus>(statuses.Count);

        foreach (var s in statuses)
        {
            clean.Add(s with
            {
                Id = Rename(s.Id, key),
                ChainedStatus = s.ChainedStatus == Guid.Empty ? Guid.Empty : Rename(s.ChainedStatus, key),
                CustomFxPath = "",
                Applier = "",
                Dispeller = "",
            });
        }

        return Convert.ToBase64String(MoodlesCodec.Encode(clean));
    }

    public static bool IsSanitizedAndBounded(string base64)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return false;
        }

        return MoodlesCodec.TryDecode(raw, out var statuses)
            && statuses.All(s => s.Applier.Length == 0 && s.Dispeller.Length == 0 && s.CustomFxPath.Length == 0);
    }

    private static Guid Rename(Guid original, ReadOnlySpan<byte> key)
    {
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(key, original.ToByteArray(), mac);
        return new Guid(mac[..16]);
    }
}
```

- [ ] **Step 6: Vérifier que ça passe**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter MoodlesCodecTests`
Expected: PASS. Si l'aller-retour octet pour octet échoue, afficher les deux tableaux avec `Convert.ToHexString` pour trouver l'écart, corriger le codec (jamais le test) et relancer.

- [ ] **Step 7: Vérifier que le noyau reste sans dépendance**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ArchitectureTests && grep -rn "using MemoryPack" Linkpearl/Core`
Expected: tests verts, et `grep` ne rend rien.

- [ ] **Step 8: Commit**

```bash
git add Linkpearl/Core/Manifest/MoodlesCodec.cs Linkpearl/Core/Manifest/MoodlesSanitizer.cs Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj Linkpearl.Core.Tests/Manifest/MoodlesCodecTests.cs
git commit -m "feat(core): Moodles décodé et nettoyé des noms, du VFX et des GUID qui relient les personnages"
```

---

### Task 5: Validation des extras à la réception

**Files:**
- Create: `Linkpearl/Core/Safety/ExtrasValidator.cs`
- Modify: `Linkpearl/Core/Safety/Quotas.cs`
- Modify: `Linkpearl/Core/Safety/ManifestValidator.cs` (appel juste avant le `return true` final de `TryAccept`)
- Test: `Linkpearl.Core.Tests/Safety/ExtrasValidatorTests.cs`

**Interfaces:**
- Consumes: `JsonShape.IsObject`, `HeelsSanitizer.Removed`, `PetNicknamesData.IsNeutralAndBounded`, `MoodlesSanitizer.IsSanitizedAndBounded`.
- Produces: `ExtrasValidator.TryAccept(CharacterExtras extras, Quotas quotas, out string? rejection) : bool` ; dans `Quotas` : `MaxCustomizePlusChars = 64 * 1024`, `MaxHeelsChars = 16 * 1024`, `MaxHonorificChars = 4 * 1024`, `MaxHonorificTitleLength = 32`, `MaxMoodlesChars = 32 * 1024`, `MaxPetNicknamesChars = 16 * 1024`, `MaxExtrasJsonDepth = 8`.

- [ ] **Step 1: Tests qui échouent**

```csharp
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public class ExtrasValidatorTests
{
    private static readonly Quotas Q = Quotas.Default;

    private static bool Accept(CharacterExtras extras) => ExtrasValidator.TryAccept(extras, Q, out _);

    private static string Pet() => PetNicknamesData.Neutralize(Convert.ToBase64String(Encoding.Unicode.GetBytes(
        string.Join("\r\n", PetNicknamesData.Header, "Nom", "73", "123", "411^2^Sparky^null^null"))))!;

    private static string Moodles() => MoodlesSanitizer.Sanitize(
        Convert.ToBase64String(MoodlesCodec.Encode([])), MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32)))!;

    [Fact]
    public void Des_extras_vides_passent() => Assert.True(Accept(CharacterExtras.None));

    [Fact]
    public void Des_extras_valides_passent()
        => Assert.True(Accept(new CharacterExtras(
            "{\"Bones\":{}}", "{\"DefaultOffset\":0.1}", "{\"Title\":\"le Voyageur\"}", Moodles(), Pet())));

    [Fact]
    public void CustomizePlus_au_plafond_passe_et_au_dessus_non()
    {
        static string Padded(int length) => "{\"a\":\"" + new string('x', length - 8) + "\"}";

        Assert.True(Accept(CharacterExtras.None with { CustomizePlus = Padded(Q.MaxCustomizePlusChars) }));
        Assert.False(Accept(CharacterExtras.None with { CustomizePlus = Padded(Q.MaxCustomizePlusChars + 1) }));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("[]")]
    [InlineData("{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":1}}}}}}}}}")]
    public void Un_json_invalide_ou_trop_profond_est_refuse(string json)
        => Assert.False(Accept(CharacterExtras.None with { CustomizePlus = json }));

    [Fact]
    public void Des_talons_non_nettoyes_sont_refuses()
        => Assert.False(Accept(CharacterExtras.None with { Heels = "{\"DefaultOffset\":0.1,\"Tags\":{}}" }));

    [Theory]
    [InlineData("{\"Title\":\"trente-trois caractères, un de trop\"}")]
    [InlineData("{\"Title\":\"a\\u0007b\"}")]
    [InlineData("{\"Title\":42}")]
    public void Un_titre_trop_long_ou_avec_un_controle_est_refuse(string json)
        => Assert.False(Accept(CharacterExtras.None with { Honorific = json }));

    [Fact]
    public void Des_moodles_non_nettoyes_sont_refuses()
    {
        var raw = Convert.ToBase64String(MoodlesCodec.Encode(
            [new MoodleStatus(Guid.NewGuid(), 1, "t", "d", "", 0, 0, 0, 1, 0, Guid.Empty, 0, "Nom@Monde", "")]));

        Assert.False(Accept(CharacterExtras.None with { Moodles = raw }));
    }

    [Fact]
    public void Des_surnoms_non_neutralises_sont_refuses()
        => Assert.False(Accept(CharacterExtras.None with
        {
            PetNicknames = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                string.Join("\r\n", PetNicknamesData.Header, "Nom", "73", "123"))),
        }));

    [Fact]
    public void Un_seul_champ_fautif_fait_refuser_le_manifeste_entier()
    {
        var manifest = new CharacterManifest(CharacterManifest.CurrentVersion, [], "", null,
            new CharacterExtras("{\"Bones\":{}}", null, "{\"Title\":\"a\\u0007\"}", null, null));

        Assert.False(ManifestValidator.TryAccept(manifest, Q, out var why));
        Assert.Contains("Honorific", why!);
    }
}
```

Note : le premier cas de titre fait 35 caractères, au-delà des 32 permis.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ExtrasValidatorTests`
Expected: FAIL, `ExtrasValidator` introuvable.

- [ ] **Step 3: Ajouter les plafonds**

Dans `Quotas.cs`, avant la fin du record :

```csharp
    /// <summary>Profil Customize+, JSON des os : de 1 à 15 Kio relevés, marge large.</summary>
    public int MaxCustomizePlusChars { get; init; } = 64 * 1024;

    /// <summary>Configuration SimpleHeels nettoyée : moins de 1 Kio relevé.</summary>
    public int MaxHeelsChars { get; init; } = 16 * 1024;

    /// <summary>Titre Honorific : moins de 250 octets relevés.</summary>
    public int MaxHonorificChars { get; init; } = 4 * 1024;

    /// <summary>Honorific refuse lui-même d'afficher plus de 32 caractères.</summary>
    public int MaxHonorificTitleLength { get; init; } = 32;

    /// <summary>Moodles en base64 : de 0,5 à 3 Kio relevés.</summary>
    public int MaxMoodlesChars { get; init; } = 32 * 1024;

    /// <summary>PetNicknames en base64 : de 0,5 à 3 Kio relevés.</summary>
    public int MaxPetNicknamesChars { get; init; } = 16 * 1024;

    /// <summary>Profondeur maximale des extras JSON.</summary>
    public int MaxExtrasJsonDepth { get; init; } = 8;
```

- [ ] **Step 4: Implémenter le validateur**

```csharp
using System.Text.Json;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Les extras d'un manifeste reçu : forme et taille, jamais le sens.
/// </summary>
/// <remarks>
/// Ces chaînes partent telles quelles dans d'autres plugins, qui les parsent
/// sans l'hypothèse qu'un pair les a écrites. On borne donc ce qui peut
/// l'être, et l'on vérifie que ce que l'émetteur devait nettoyer l'a bien été :
/// un client modifié pourrait sinon nous faire poser un nom, un ContentId ou un
/// VFX.
/// </remarks>
public static class ExtrasValidator
{
    public static bool TryAccept(CharacterExtras extras, Quotas quotas, out string? rejection)
    {
        rejection = Check(extras, quotas);
        return rejection is null;
    }

    private static string? Check(CharacterExtras extras, Quotas q)
    {
        if (extras.CustomizePlus is { } customize)
        {
            if (customize.Length > q.MaxCustomizePlusChars)
                return $"Customize+ : plafond dépassé (plafond {q.MaxCustomizePlusChars})";

            if (JsonShape.IsObject(customize, q.MaxExtrasJsonDepth) is false)
                return "Customize+ : JSON invalide ou trop profond";
        }

        if (extras.Heels is { } heels)
        {
            if (heels.Length > q.MaxHeelsChars)
                return $"SimpleHeels : plafond dépassé (plafond {q.MaxHeelsChars})";

            if (JsonShape.IsObject(heels, q.MaxExtrasJsonDepth) is false)
                return "SimpleHeels : JSON invalide ou trop profond";

            using var doc = JsonDocument.Parse(heels);

            if (HeelsSanitizer.Removed.Any(name => doc.RootElement.TryGetProperty(name, out _)))
                return "SimpleHeels : champs qui auraient dû être retirés à l'envoi";
        }

        if (extras.Honorific is { } honorific)
        {
            if (honorific.Length > q.MaxHonorificChars)
                return $"Honorific : plafond dépassé (plafond {q.MaxHonorificChars})";

            if (JsonShape.IsObject(honorific, q.MaxExtrasJsonDepth) is false)
                return "Honorific : JSON invalide ou trop profond";

            using var doc = JsonDocument.Parse(honorific);

            if (doc.RootElement.TryGetProperty("Title", out var title))
            {
                if (title.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    return "Honorific : titre non textuel";

                var text = title.GetString() ?? "";

                if (text.Length > q.MaxHonorificTitleLength || text.Any(char.IsControl))
                    return "Honorific : titre trop long ou avec un caractère de contrôle";
            }
        }

        if (extras.Moodles is { } moodles)
        {
            if (moodles.Length > q.MaxMoodlesChars)
                return $"Moodles : plafond dépassé (plafond {q.MaxMoodlesChars})";

            if (MoodlesSanitizer.IsSanitizedAndBounded(moodles) is false)
                return "Moodles : format invalide ou données non nettoyées";
        }

        if (extras.PetNicknames is { } pets)
        {
            if (pets.Length > q.MaxPetNicknamesChars)
                return $"PetNicknames : plafond dépassé (plafond {q.MaxPetNicknamesChars})";

            if (PetNicknamesData.IsNeutralAndBounded(pets) is false)
                return "PetNicknames : format invalide ou identité non neutralisée";
        }

        return null;
    }
}
```

- [ ] **Step 5: Brancher dans `ManifestValidator`**

Dans `ManifestValidator.TryAccept`, juste avant le `rejection = null; return true;` final :

```csharp
        if (ExtrasValidator.TryAccept(manifest.ExtrasOrNone, quotas, out var extrasRejection) is false)
        {
            rejection = extrasRejection;
            return false;
        }
```

- [ ] **Step 6: Vérifier que tout passe**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Core/Safety/ExtrasValidator.cs Linkpearl/Core/Safety/Quotas.cs Linkpearl/Core/Safety/ManifestValidator.cs Linkpearl.Core.Tests/Safety/ExtrasValidatorTests.cs
git commit -m "feat(safety): valider les extras reçus, champ par champ, et tout refuser au moindre écart"
```

---

### Task 6: Application des seuls extras dans le moteur

**Files:**
- Create: `Linkpearl/Core/Sync/ExtrasDiff.cs`
- Modify: `Linkpearl/Core/Sync/ISyncSurface.cs` (interface `IRemoteApplicator`)
- Modify: `Linkpearl/Core/Sync/SyncEngine.cs` (boucle de décision vers les lignes 515-535, `ApplyAsync`, `Runtime`)
- Modify: `Linkpearl.Core.Tests/Sync/SyncEngineTests.cs` (`RecordingApplicator`, `TwoEngines`, nouveaux tests)
- Modify: `Linkpearl.Harness/FakePeerRun.cs` (`NarratingApplicator`)
- Modify: `Linkpearl/Integration/RemoteApplicator.cs` (bouchon provisoire)
- Test: `Linkpearl.Core.Tests/Sync/ExtrasDiffTests.cs`

**Interfaces:**
- Consumes: `CharacterExtras`, `CharacterManifest.ExtrasOrNone`, `ManifestCodec.HashOf`.
- Produces:
  - `public sealed record ExtrasChange(bool CustomizePlus, bool Heels, bool Honorific, bool Moodles, bool PetNicknames)` avec `static ExtrasChange All` et `bool Any`.
  - `ExtrasDiff.OnlyExtrasDiffer(CharacterManifest before, CharacterManifest after) : bool` ; `ExtrasDiff.Between(CharacterExtras before, CharacterExtras after) : ExtrasChange`.
  - `IRemoteApplicator.ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct) : Task`.

- [ ] **Step 1: Tests de la comparaison, qui échouent**

```csharp
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

public class ExtrasDiffTests
{
    private static CharacterManifest M(string meta, CharacterExtras? extras)
        => new(CharacterManifest.CurrentVersion,
               [new FileReplacement(["chara/x.mdl"], BlobHash.OfContent("x"u8), 1)], meta, null, extras);

    [Fact]
    public void Un_titre_seul_change_ne_demande_que_les_extras()
    {
        var before = M("", new CharacterExtras(null, null, "{\"Title\":\"a\"}", null, null));
        var after = M("", new CharacterExtras(null, null, "{\"Title\":\"b\"}", null, null));

        Assert.True(ExtrasDiff.OnlyExtrasDiffer(before, after));
        Assert.Equal(new ExtrasChange(false, false, true, false, false),
                     ExtrasDiff.Between(before.ExtrasOrNone, after.ExtrasOrNone));
    }

    [Fact]
    public void Des_fichiers_changes_demandent_une_application_complete()
        => Assert.False(ExtrasDiff.OnlyExtrasDiffer(M("", null), M("AAAA", null)));

    [Fact]
    public void Un_extra_qui_disparait_est_un_changement()
        => Assert.True(ExtrasDiff.Between(
            new CharacterExtras("{}", null, null, null, null), CharacterExtras.None).CustomizePlus);

    [Fact]
    public void Rien_de_change_ne_change_rien()
        => Assert.False(ExtrasDiff.Between(CharacterExtras.None, CharacterExtras.None).Any);
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ExtrasDiffTests`
Expected: FAIL.

- [ ] **Step 3: Implémenter la comparaison**

`Linkpearl/Core/Sync/ExtrasDiff.cs` :

```csharp
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Sync;

/// <summary>Quels extras ont changé entre deux manifestes.</summary>
public sealed record ExtrasChange(bool CustomizePlus, bool Heels, bool Honorific, bool Moodles, bool PetNicknames)
{
    public static ExtrasChange All { get; } = new(true, true, true, true, true);

    public bool Any => CustomizePlus || Heels || Honorific || Moodles || PetNicknames;
}

/// <summary>
/// Décide si un nouveau manifeste peut se poser sans redessin.
/// </summary>
/// <remarks>
/// Un titre Honorific ou un statut Moodles changent souvent, et un redessin
/// fait clignoter le personnage entier. Quand fichiers, métadonnées et état
/// Glamourer sont identiques, seuls les plugins concernés sont appelés.
/// </remarks>
public static class ExtrasDiff
{
    public static bool OnlyExtrasDiffer(CharacterManifest before, CharacterManifest after)
        => ManifestCodec.HashOf(before with { Extras = null, Version = CharacterManifest.CurrentVersion })
        == ManifestCodec.HashOf(after with { Extras = null, Version = CharacterManifest.CurrentVersion });

    public static ExtrasChange Between(CharacterExtras before, CharacterExtras after)
        => new(before.CustomizePlus != after.CustomizePlus,
               before.Heels != after.Heels,
               before.Honorific != after.Honorific,
               before.Moodles != after.Moodles,
               before.PetNicknames != after.PetNicknames);
}
```

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter ExtrasDiffTests`
Expected: PASS.

- [ ] **Step 4: Étendre l'interface et les applicateurs**

Dans `ISyncSurface.cs`, dans `IRemoteApplicator`, après `ApplyAsync` :

```csharp
    /// <summary>
    /// Pose seulement les extras qui ont changé, sans redessin, sur un objet où
    /// l'apparence est déjà posée.
    /// </summary>
    Task ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct);
```

Dans `SyncEngineTests.cs`, dans `RecordingApplicator` :

```csharp
    public List<(PeerId Peer, CharacterExtras Extras, ExtrasChange Change)> ExtrasApplied { get; } = [];

    public Task ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct)
    {
        ExtrasApplied.Add((peer, extras, change));
        return Task.CompletedTask;
    }
```

Dans `Linkpearl.Harness/FakePeerRun.cs`, dans `NarratingApplicator` :

```csharp
    public Task ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct)
    {
        Console.WriteLine($"  Extras posés sans redessin : {change}.");
        return Task.CompletedTask;
    }
```

Dans `Linkpearl/Integration/RemoteApplicator.cs`, pour que le plugin compile dès ce commit :

```csharp
    public Task ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct)
        => Task.CompletedTask;   // remplacé en tâche 9, quand les IPC des plugins voisins existent
```

- [ ] **Step 5: Tests du moteur, qui échouent**

Dans `SyncEngineTests.cs`, `TwoEnginesAsync` crée `new FixedAppearance(manifest, AlicePrint)` pour Alice : le mettre dans une variable `aliceAppearance`, la passer au moteur d'Alice, et ajouter au record `TwoEngines` un dernier champ `FixedAppearance AliceAppearance` (le passer au constructeur). Puis ajouter :

```csharp
    [Fact]
    public async Task Un_changement_d_extras_seul_se_pose_sans_application_complete()
    {
        await using var world = await TwoEnginesAsync();

        IReadOnlyList<VisiblePlayer> sees = [new VisiblePlayer(new GameObjectRef(4, 100), AlicePrint)];

        Assert.True(await world.SettleAsync(() => world.BobApplicator.Applied.Count > 0, [], sees),
            "l'apparence n'a jamais été posée : " + world.Describe());

        world.AliceAppearance.Manifest = world.AliceAppearance.Manifest! with
        {
            Extras = CharacterExtras.None with { Honorific = "{\"Title\":\"le Voyageur\"}" },
        };

        Assert.True(await world.SettleAsync(() => world.BobApplicator.ExtrasApplied.Count > 0, [], sees),
            "les extras n'ont jamais été posés : " + world.Describe());

        Assert.Single(world.BobApplicator.Applied);
        Assert.True(world.BobApplicator.ExtrasApplied[0].Change.Honorific);
        Assert.False(world.BobApplicator.ExtrasApplied[0].Change.CustomizePlus);
    }

    [Fact]
    public async Task Des_fichiers_changes_redemandent_une_application_complete()
    {
        await using var world = await TwoEnginesAsync();

        IReadOnlyList<VisiblePlayer> sees = [new VisiblePlayer(new GameObjectRef(4, 100), AlicePrint)];

        Assert.True(await world.SettleAsync(() => world.BobApplicator.Applied.Count > 0, [], sees));

        world.AliceAppearance.Manifest = world.AliceAppearance.Manifest! with
        {
            MetaManipulations = "AAAA",
            Extras = CharacterExtras.None with { Honorific = "{\"Title\":\"b\"}" },
        };

        Assert.True(await world.SettleAsync(() => world.BobApplicator.Applied.Count > 1, [], sees),
            "la nouvelle apparence n'a pas été reposée : " + world.Describe());
        Assert.Empty(world.BobApplicator.ExtrasApplied);
    }
```

Le moteur d'Alice compare `ILocalAppearance.CurrentAsync` par référence : la nouvelle instance de manifeste le fait réannoncer.

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter SyncEngineTests`
Expected: FAIL sur le premier (`Applied` vaut 2, `ExtrasApplied` reste vide).

- [ ] **Step 6: Implémenter dans le moteur**

Dans la classe `Runtime` de `SyncEngine`, ajouter :

```csharp
        /// <summary>Le manifeste posé, pour savoir ce qu'un nouveau change.</summary>
        public CharacterManifest? AppliedValue { get; set; }
```

Dans la boucle de décision, remplacer `runtime.Work = ApplyAsync(id, runtime, target, manifest, hash);` par :

```csharp
                // Même objet, mêmes fichiers : seuls les plugins voisins ont
                // quelque chose à reposer, et un redessin ferait clignoter le
                // personnage entier pour un titre qui change.
                if (runtime.AppliedOn == target
                    && runtime.AppliedValue is { } before
                    && ExtrasDiff.OnlyExtrasDiffer(before, manifest))
                {
                    var change = ExtrasDiff.Between(before.ExtrasOrNone, manifest.ExtrasOrNone);
                    runtime.Work = ApplyExtrasAsync(id, runtime, target, manifest, change, hash);
                }
                else
                {
                    runtime.Work = ApplyAsync(id, runtime, target, manifest, hash);
                }
```

Dans le `ApplyAsync` privé du moteur, après `runtime.AppliedManifest = hash;`, ajouter `runtime.AppliedValue = manifest;`. À chacun des trois endroits où `runtime.AppliedManifest = null;` est écrit, ajouter `runtime.AppliedValue = null;`.

Ajouter la méthode :

```csharp
    private async Task ApplyExtrasAsync(
        PeerId id, Runtime runtime, GameObjectRef target, CharacterManifest manifest, ExtrasChange change, BlobHash hash)
    {
        try
        {
            await _applicator.ApplyExtrasAsync(id, target, manifest.ExtrasOrNone, change, _life.Token).ConfigureAwait(false);

            runtime.AppliedManifest = hash;
            runtime.AppliedValue = manifest;

            _log.Info($"{runtime.Pair.DisplayName} : extras reposés sans redessin.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            runtime.LastFailure = $"extras en échec : {e.Message}";
            _log.Warning($"{runtime.Pair.DisplayName} : extras en échec.", e);
        }
    }
```

- [ ] **Step 7: Vérifier**

Run: `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && dotnet build Linkpearl.Harness -c Release && dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: tests verts, les deux builds réussissent sans warning.

- [ ] **Step 8: Commit**

```bash
git add Linkpearl/Core/Sync/ExtrasDiff.cs Linkpearl/Core/Sync/ISyncSurface.cs Linkpearl/Core/Sync/SyncEngine.cs Linkpearl.Core.Tests/Sync/ExtrasDiffTests.cs Linkpearl.Core.Tests/Sync/SyncEngineTests.cs Linkpearl.Harness/FakePeerRun.cs Linkpearl/Integration/RemoteApplicator.cs
git commit -m "feat(sync): reposer les seuls extras, sans redessin, quand rien d'autre ne change"
```

---

### Task 7: Les cinq IPC côté adaptateur

**Files:**
- Create: `Linkpearl/Integration/Extras/CustomizePlusIpc.cs`
- Create: `Linkpearl/Integration/Extras/HeelsIpc.cs`
- Create: `Linkpearl/Integration/Extras/HonorificIpc.cs`
- Create: `Linkpearl/Integration/Extras/MoodlesIpc.cs`
- Create: `Linkpearl/Integration/Extras/PetNicknamesIpc.cs`

**Interfaces:**
- Consumes: `PetNicknamesData.TrySplit` et `Join` (tâche 3).
- Produces, pour chaque `XxxIpc : IDisposable` construite avec `IDalamudPluginInterface pi` : `bool IsAvailable()` (jamais d'exception), une lecture locale (`ReadLocal()`, ou `ReadLocal(nint localAddress)` pour Moodles), `void Apply(IGameObject target, string data)`, `void Clear(IGameObject target)`, `event Action? Changed` déjà filtré sur le joueur local, et `event Action? Ready` pour Honorific, Moodles et PetNicknames. `MoodlesIpc` prend en plus `Func<nint> localAddress`. Tous les appels se font sur le thread du framework.

Libellés et signatures relevés dans le code des plugins (spec, section Adaptateur). Aucun test Linux possible : la vérification est la compilation, puis la liste d'essais en jeu (tâche 10).

- [ ] **Step 1: Customize+**

```csharp
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Customize+ 6.x, relevé dans CustomizePlus 44d2541 (Api/CustomizePlusIpc.Profile.cs).
/// </summary>
/// <remarks>
/// Le profil posé sur un pair est temporaire : Customize+ l'oublie quand
/// l'armature disparaît, et le moteur repose tout à chaque réapparition.
/// </remarks>
public sealed class CustomizePlusIpc : IDisposable
{
    private readonly ICallGateSubscriber<(int, int)> _version;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _activeProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _profile;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTemporary;
    private readonly ICallGateSubscriber<ushort, int> _deleteTemporary;
    private readonly ICallGateSubscriber<ushort, Guid, object> _onUpdate;

    public CustomizePlusIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _activeProfile = pi.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _profile = pi.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _setTemporary = pi.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _deleteTemporary = pi.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _onUpdate = pi.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _onUpdate.Subscribe(OnUpdate);
    }

    public event Action? Changed;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 6;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Le profil actif du joueur local, ou null. Code 0 : succès.</summary>
    public string? ReadLocal()
    {
        var (found, id) = _activeProfile.InvokeFunc(0);

        if (found != 0 || id is not { } profileId || profileId == Guid.Empty)
            return null;

        var (read, json) = _profile.InvokeFunc(profileId);
        return read == 0 && string.IsNullOrEmpty(json) is false ? json : null;
    }

    public void Apply(IGameObject target, string data) => _setTemporary.InvokeFunc(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _deleteTemporary.InvokeFunc(target.ObjectIndex);

    private void OnUpdate(ushort objectIndex, Guid profile)
    {
        if (objectIndex == 0)
            Changed?.Invoke();
    }

    public void Dispose() => _onUpdate.Unsubscribe(OnUpdate);
}
```

- [ ] **Step 2: SimpleHeels**

```csharp
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// SimpleHeels 2.x, relevé dans SimpleHeels 162466c (ApiProvider.cs).
/// </summary>
/// <remarks>
/// SimpleHeels garde un décalage reçu tant qu'on ne le désenregistre pas, et ne
/// se désenregistre que sur un personnage visible : voir la limite connue de
/// la spec.
/// </remarks>
public sealed class HeelsIpc : IDisposable
{
    private readonly ICallGateSubscriber<(int, int)> _version;
    private readonly ICallGateSubscriber<string> _getLocal;
    private readonly ICallGateSubscriber<int, string, object?> _register;
    private readonly ICallGateSubscriber<int, object?> _unregister;
    private readonly ICallGateSubscriber<string, object?> _localChanged;

    public HeelsIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");
        _getLocal = pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");
        _register = pi.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        _unregister = pi.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");
        _localChanged = pi.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");
        _localChanged.Subscribe(OnLocalChanged);
    }

    public event Action? Changed;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 2;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal()
    {
        var json = _getLocal.InvokeFunc();
        return string.IsNullOrEmpty(json) ? null : json;
    }

    public void Apply(IGameObject target, string data) => _register.InvokeAction(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _unregister.InvokeAction(target.ObjectIndex);

    private void OnLocalChanged(string _) => Changed?.Invoke();

    public void Dispose() => _localChanged.Unsubscribe(OnLocalChanged);
}
```

- [ ] **Step 3: Honorific**

```csharp
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Honorific 3.x, relevé dans Honorific 9d66b63 (IpcProvider.cs).
/// </summary>
/// <remarks>
/// Honorific efface de lui-même, toutes les cinq secondes, le titre d'un
/// personnage qui n'est plus visible : le moteur repose tout à la réapparition.
/// </remarks>
public sealed class HonorificIpc : IDisposable
{
    private readonly ICallGateSubscriber<(uint, uint)> _version;
    private readonly ICallGateSubscriber<string> _getLocal;
    private readonly ICallGateSubscriber<int, string, object> _set;
    private readonly ICallGateSubscriber<int, object> _clear;
    private readonly ICallGateSubscriber<string, object> _localChanged;
    private readonly ICallGateSubscriber<object> _ready;

    public HonorificIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        _getLocal = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
        _set = pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _clear = pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
        _localChanged = pi.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
        _ready = pi.GetIpcSubscriber<object>("Honorific.Ready");
        _localChanged.Subscribe(OnLocalChanged);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 3;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal()
    {
        var json = _getLocal.InvokeFunc();
        return string.IsNullOrEmpty(json) ? null : json;
    }

    public void Apply(IGameObject target, string data) => _set.InvokeAction(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _clear.InvokeAction(target.ObjectIndex);

    private void OnLocalChanged(string _) => Changed?.Invoke();

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _localChanged.Unsubscribe(OnLocalChanged);
        _ready.Unsubscribe(OnReady);
    }
}
```

- [ ] **Step 4: Moodles**

```csharp
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Moodles version 4, relevé dans Moodles f4b7578 (IPCProcessor.cs).
/// </summary>
/// <remarks>
/// Par adresse et jamais par nom : les variantes « ByName » prennent
/// « Nom@Monde ». Moodles ignore en silence un personnage qu'il n'a pas encore
/// vu s'afficher, d'où l'attente de chargement avant de poser.
/// </remarks>
public sealed class MoodlesIpc : IDisposable
{
    private readonly ICallGateSubscriber<int> _version;
    private readonly ICallGateSubscriber<nint, string> _get;
    private readonly ICallGateSubscriber<nint, string, object> _set;
    private readonly ICallGateSubscriber<nint, object> _clear;
    private readonly ICallGateSubscriber<nint, object> _modified;
    private readonly ICallGateSubscriber<object> _ready;
    private readonly Func<nint> _localAddress;

    public MoodlesIpc(IDalamudPluginInterface pi, Func<nint> localAddress)
    {
        _localAddress = localAddress;
        _version = pi.GetIpcSubscriber<int>("Moodles.Version");
        _get = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        _set = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        _clear = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");
        _modified = pi.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        _ready = pi.GetIpcSubscriber<object>("Moodles.Ready");
        _modified.Subscribe(OnModified);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc() == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal(nint localAddress)
    {
        var data = _get.InvokeFunc(localAddress);
        return string.IsNullOrEmpty(data) ? null : data;
    }

    public void Apply(IGameObject target, string data) => _set.InvokeAction(target.Address, data);

    public void Clear(IGameObject target) => _clear.InvokeAction(target.Address);

    /// <summary>Levé pour n'importe quel personnage suivi : on ne garde que le nôtre.</summary>
    private void OnModified(nint address)
    {
        if (address != nint.Zero && address == _localAddress())
            Changed?.Invoke();
    }

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _modified.Unsubscribe(OnModified);
        _ready.Unsubscribe(OnReady);
    }
}
```

- [ ] **Step 5: PetNicknames**

```csharp
using System.Globalization;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// PetNicknames (InternalName PetRenamer) 4.x, relevé dans FFXIVPetRenamer
/// 7192264 (IPC/IpcProvider.cs).
/// </summary>
/// <remarks>
/// Les données reçues sont neutralisées : ni nom, ni monde, ni ContentId. On y
/// remet ici ceux du personnage visé, lus chez nous, juste avant de poser, et
/// ils ne sortent pas de cette classe : ni journal, ni noyau. <c>SetPlayerDataV2</c>
/// vérifie que le ContentId passé est bien celui des données.
///
/// <c>ClearPlayerData</c> V1 n'est jamais employé : il retrouve l'entrée par nom
/// et effacerait un surnom que l'utilisateur aurait importé lui-même.
/// </remarks>
public sealed class PetNicknamesIpc : IDisposable
{
    private readonly ICallGateSubscriber<bool> _enabled;
    private readonly ICallGateSubscriber<(uint, uint)> _version;
    private readonly ICallGateSubscriber<string> _get;
    private readonly ICallGateSubscriber<ulong, string, object> _set;
    private readonly ICallGateSubscriber<nint, object> _clear;
    private readonly ICallGateSubscriber<string, object> _changed;
    private readonly ICallGateSubscriber<object> _ready;

    public PetNicknamesIpc(IDalamudPluginInterface pi)
    {
        _enabled = pi.GetIpcSubscriber<bool>("PetRenamer.IsEnabled");
        _version = pi.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
        _get = pi.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
        _set = pi.GetIpcSubscriber<ulong, string, object>("PetRenamer.SetPlayerDataV2");
        _clear = pi.GetIpcSubscriber<nint, object>("PetRenamer.ClearPlayerDataV2");
        _changed = pi.GetIpcSubscriber<string, object>("PetRenamer.OnPlayerDataChanged");
        _ready = pi.GetIpcSubscriber<object>("PetRenamer.OnReady");
        _changed.Subscribe(OnChanged);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _enabled.InvokeFunc() && _version.InvokeFunc().Item1 == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal()
    {
        var data = _get.InvokeFunc();
        return string.IsNullOrEmpty(data) ? null : data;
    }

    public unsafe void Apply(IGameObject target, string neutral)
    {
        if (target is not IPlayerCharacter player
            || PetNicknamesData.TrySplit(neutral, out var lines, out var separator) is false)
            return;

        var contentId = ((Character*)player.Address)->ContentId;

        lines[1] = player.Name.TextValue;
        lines[2] = player.HomeWorld.RowId.ToString(CultureInfo.InvariantCulture);
        lines[3] = contentId.ToString(CultureInfo.InvariantCulture);

        _set.InvokeAction(contentId, PetNicknamesData.Join(lines, separator));
    }

    public void Clear(IGameObject target) => _clear.InvokeAction(target.Address);

    private void OnChanged(string _) => Changed?.Invoke();

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _changed.Unsubscribe(OnChanged);
        _ready.Unsubscribe(OnReady);
    }
}
```

Si `ContentId` n'est pas un champ de `Character` dans la version installée de FFXIVClientStructs, le chercher avec `grep -o 'F:FFXIVClientStructs.FFXIV.Client.Game.Character.[A-Za-z]*.ContentId' ~/.xlcore/dalamud/Hooks/dev/FFXIVClientStructs.xml` et adapter le type du pointeur.

- [ ] **Step 6: Compiler**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Expected: succès, sans warning.

- [ ] **Step 7: Commit**

```bash
git add Linkpearl/Integration/Extras/
git commit -m "feat(integration): appels IPC de Customize+, SimpleHeels, Honorific, Moodles et PetNicknames"
```

---

### Task 8: Lire et nettoyer nos propres extras

**Files:**
- Create: `Linkpearl/Integration/Extras/ExtrasIpc.cs`
- Modify: `Linkpearl/Integration/LocalAppearance.cs` (constructeur, `ReadWhenDrawnAsync`, `RebuildAsync`)
- Modify: `Linkpearl/Plugin.cs` (construction, abonnement, `MoodlesKey`, `Dispose`)

**Interfaces:**
- Consumes: les cinq `XxxIpc` (tâche 7), `HeelsSanitizer`, `PetNicknamesData`, `MoodlesSanitizer` (tâches 2 à 4), `ExtrasChange` (tâche 6).
- Produces: `ExtrasIpc : IDisposable` avec `CharacterExtras ReadLocal(IGameObject local)` (brut, non nettoyé), `void Apply(IGameObject target, CharacterExtras extras, ExtrasChange change)`, `void Clear(IGameObject target)`, `event Action? Changed`, `event Action? Ready`. Le constructeur de `LocalAppearance` devient `(PenumbraIpc penumbra, GlamourerIpc glamourer, IFramework framework, IObjectTable objects, ExtrasIpc extras, Func<byte[]?> moodlesKey, IBlobStore store, IPluginLog log)`.

- [ ] **Step 1: Écrire l'agrégateur**

```csharp
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Les cinq plugins voisins, derrière une seule façade.
/// </summary>
/// <remarks>
/// Un plugin absent, d'une version qui ne convient pas, ou qui lève en cours
/// d'appel, n'empêche jamais les autres : chacun est essayé à part, et son
/// échec n'est signalé qu'une fois dans le journal, pas à chaque image.
///
/// Tout appel se fait depuis le thread du framework.
/// </remarks>
public sealed class ExtrasIpc : IDisposable
{
    private readonly CustomizePlusIpc _customize;
    private readonly HeelsIpc _heels;
    private readonly HonorificIpc _honorific;
    private readonly MoodlesIpc _moodles;
    private readonly PetNicknamesIpc _pets;
    private readonly IPluginLog _log;
    private readonly HashSet<string> _reported = [];

    public ExtrasIpc(IDalamudPluginInterface pi, IObjectTable objects, IPluginLog log)
    {
        _log = log;
        _customize = new CustomizePlusIpc(pi);
        _heels = new HeelsIpc(pi);
        _honorific = new HonorificIpc(pi);
        _moodles = new MoodlesIpc(pi, () => objects.LocalPlayer?.Address ?? nint.Zero);
        _pets = new PetNicknamesIpc(pi);

        _customize.Changed += RaiseChanged;
        _heels.Changed += RaiseChanged;
        _honorific.Changed += RaiseChanged;
        _moodles.Changed += RaiseChanged;
        _pets.Changed += RaiseChanged;

        _honorific.Ready += RaiseReady;
        _moodles.Ready += RaiseReady;
        _pets.Ready += RaiseReady;
    }

    /// <summary>Notre état a changé chez l'un d'eux : l'apparence est à reconstruire.</summary>
    public event Action? Changed;

    /// <summary>Un plugin vient de (re)démarrer : ce qui était posé chez lui est perdu.</summary>
    public event Action? Ready;

    public CharacterExtras ReadLocal(IGameObject local) => new(
        Read("Customize+", _customize.IsAvailable, _customize.ReadLocal),
        Read("SimpleHeels", _heels.IsAvailable, _heels.ReadLocal),
        Read("Honorific", _honorific.IsAvailable, _honorific.ReadLocal),
        Read("Moodles", _moodles.IsAvailable, () => _moodles.ReadLocal(local.Address)),
        Read("PetNicknames", _pets.IsAvailable, _pets.ReadLocal));

    public void Apply(IGameObject target, CharacterExtras extras, ExtrasChange change)
    {
        if (change.CustomizePlus)
            Put("Customize+", _customize.IsAvailable, extras.CustomizePlus, d => _customize.Apply(target, d), () => _customize.Clear(target));

        if (change.Heels)
            Put("SimpleHeels", _heels.IsAvailable, extras.Heels, d => _heels.Apply(target, d), () => _heels.Clear(target));

        if (change.Honorific)
            Put("Honorific", _honorific.IsAvailable, extras.Honorific, d => _honorific.Apply(target, d), () => _honorific.Clear(target));

        if (change.Moodles)
            Put("Moodles", _moodles.IsAvailable, extras.Moodles, d => _moodles.Apply(target, d), () => _moodles.Clear(target));

        if (change.PetNicknames)
            Put("PetNicknames", _pets.IsAvailable, extras.PetNicknames, d => _pets.Apply(target, d), () => _pets.Clear(target));
    }

    public void Clear(IGameObject target) => Apply(target, CharacterExtras.None, ExtrasChange.All);

    private string? Read(string name, Func<bool> available, Func<string?> read)
    {
        try
        {
            return available() ? read() : null;
        }
        catch (Exception e)
        {
            ReportOnce(name, e);
            return null;
        }
    }

    private void Put(string name, Func<bool> available, string? data, Action<string> apply, Action clear)
    {
        try
        {
            if (available() is false)
                return;

            if (data is null)
                clear();
            else
                apply(data);
        }
        catch (Exception e)
        {
            ReportOnce(name, e);
        }
    }

    private void ReportOnce(string name, Exception e)
    {
        if (_reported.Add(name))
            _log.Warning(e, $"{name} ne répond pas comme attendu, ignoré.");
    }

    private void RaiseChanged() => Changed?.Invoke();

    private void RaiseReady() => Ready?.Invoke();

    public void Dispose()
    {
        _customize.Dispose();
        _heels.Dispose();
        _honorific.Dispose();
        _moodles.Dispose();
        _pets.Dispose();
    }
}
```

- [ ] **Step 2: Lire nos extras avec les ressources**

Dans `LocalAppearance`, ajouter `using Linkpearl.Integration.Extras;`, les champs `private readonly ExtrasIpc _extras;` et `private readonly Func<byte[]?> _moodlesKey;`, les deux paramètres de constructeur juste après `IObjectTable objects`, et leurs affectations.

Remplacer `ReadWhenDrawnAsync` par :

```csharp
    private async Task<(IReadOnlyDictionary<string, HashSet<string>>? Resources, string Meta, string? Glamourer, CharacterExtras Extras)?>
        ReadWhenDrawnAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var snapshot = await _framework.RunOnFrameworkThread(() =>
                _objects[PlayerIndex] is { } local && DrawReadiness.IsReady(local)
                    ? ((IReadOnlyDictionary<string, HashSet<string>>?, string, string?, CharacterExtras)?)(
                        _penumbra.ResourcePathsOf(PlayerIndex),
                        _penumbra.MetaManipulations(),
                        _glamourer.StateOf(PlayerIndex),
                        _extras.ReadLocal(local))
                    : null).ConfigureAwait(false);

            if (snapshot is not null)
                return snapshot;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return null;
    }
```

Dans `RebuildAsync`, déstructurer `var (resources, meta, glamourer, rawExtras) = snapshot.Value;`. Juste après `var build = ManifestBuilder.Build(resolved, meta, glamourer, Quotas.Default);`, introduire le manifeste final :

```csharp
        // Nettoyés ici, hors du thread du jeu, avant de rien annoncer : un
        // nom, un ContentId ou un GUID qui relie nos personnages ne quitte pas
        // cette machine. Un extra qui ne se nettoie pas est omis plutôt
        // qu'envoyé tel quel.
        var extras = Clean(rawExtras);
        var manifest = build.Manifest with { Extras = extras.IsEmpty ? null : extras };
```

Puis, dans la suite de `RebuildAsync`, remplacer chaque `build.Manifest` par `manifest` (le contrôle « apparence vide », la comparaison d'empreinte, `_current = ...` et la description). `build.Skipped` reste inchangé.

Ajouter la méthode :

```csharp
    private CharacterExtras Clean(CharacterExtras raw)
    {
        var key = _moodlesKey();

        return new CharacterExtras(
            raw.CustomizePlus,
            raw.Heels is { } heels ? HeelsSanitizer.Sanitize(heels) : null,
            raw.Honorific,
            raw.Moodles is { } moodles && key is not null ? MoodlesSanitizer.Sanitize(moodles, key) : null,
            raw.PetNicknames is { } pets ? PetNicknamesData.Neutralize(pets) : null);
    }
```

- [ ] **Step 3: Brancher dans `Plugin.cs`**

Ajouter `using Linkpearl.Integration.Extras;` et le champ `private readonly ExtrasIpc _extras;`. Juste avant la construction de `_appearance` :

```csharp
        _extras = new ExtrasIpc(PluginInterface, Objects, Log);
```

Remplacer la construction de `_appearance` par :

```csharp
        _appearance = new LocalAppearance(
            penumbra, glamourer, Framework, Objects, _extras, MoodlesKey, _cache, Log);
```

Après l'abonnement `glamourer.Changed += ...`, ajouter :

```csharp
        // Les plugins voisins signalent eux-mêmes nos changements : titre,
        // statuts, proportions. La rafale passe par le même anti-rebond.
        _extras.Changed += _appearanceChanged.Signal;
```

Ajouter la méthode :

```csharp
    /// <summary>
    /// La clé qui renomme nos GUID Moodles, propre au personnage connecté.
    /// </summary>
    /// <remarks>
    /// Dérivée de l'identité du personnage : deux personnages d'une même
    /// personne donnent des GUID qu'on ne peut pas relier.
    /// </remarks>
    private byte[]? MoodlesKey()
        => _pairing.Identity is { } identity
            ? MoodlesSanitizer.KeyFor(identity.Key.ExportParameters(includePrivateParameters: true).D!)
            : null;
```

Dans `Dispose`, juste après `_appearance.Dispose();`, ajouter `_extras.Dispose();`.

- [ ] **Step 4: Compiler, tester, déployer**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release && dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && ./scripts/deploy-plugin-dev.sh`
Expected: succès sans warning, tests verts, déploiement effectué.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Integration/Extras/ExtrasIpc.cs Linkpearl/Integration/LocalAppearance.cs Linkpearl/Plugin.cs
git commit -m "feat(integration): annoncer nos extras, nettoyés, avec l'apparence"
```

---

### Task 9: Poser et retirer les extras d'un pair

**Files:**
- Modify: `Linkpearl/Integration/RemoteApplicator.cs`
- Modify: `Linkpearl/Plugin.cs` (argument du constructeur, abonnement `Ready`)

**Interfaces:**
- Consumes: `ExtrasIpc` (tâche 8), `ExtrasChange` (tâche 6), `DrawReadiness.IsReady` (existant).
- Produces: `RemoteApplicator.ApplyExtrasAsync` réel ; le constructeur de `RemoteApplicator` prend `ExtrasIpc extras` juste après `GlamourerIpc glamourer`.

- [ ] **Step 1: Poser les extras après Glamourer**

Dans `RemoteApplicator` : `using Linkpearl.Integration.Extras;`, champ `private readonly ExtrasIpc _extras;`, paramètre de constructeur juste après `glamourer`, affectation. Le record privé devient :

```csharp
    private sealed record AppliedPeer(Guid Collection, GameObjectRef? Object, bool GlamourerTouched, bool ExtrasTouched = false);
```

À la fin de `ApplyAsync`, remplacer tout le bloc qui commence par `if (plan!.GlamourerState is not { } state)` jusqu'à la fin de la méthode par :

```csharp
        if (plan!.GlamourerState is { } state)
        {
            // Après le redessin, jamais avant. Verrouillé sous notre clé, pour que
            // l'automation de Glamourer chez nous n'écrase pas ce que le pair a
            // choisi de montrer.
            await _framework.RunOnFrameworkThread(() =>
            {
                if (Resolve(target) is false)
                    return;

                _glamourer.ApplyStateLocked(state, target.ObjectIndex);
                Remember(peer, applied => applied with { GlamourerTouched = true });
            }).ConfigureAwait(false);
        }

        // Les extras en dernier, sur un personnage entièrement chargé : Moodles
        // ignore en silence celui qu'il n'a pas encore vu s'afficher.
        await ApplyExtrasAsync(peer, target, manifest.ExtrasOrNone, ExtrasChange.All, ct).ConfigureAwait(false);
```

Remplacer le bouchon de la tâche 6 par :

```csharp
    public async Task ApplyExtrasAsync(
        PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct)
    {
        if (change.Any is false)
            return;

        // Dix secondes au plus, comme pour notre propre personnage.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var done = await _framework.RunOnFrameworkThread(() =>
            {
                if (Resolve(target) is false)
                    return true;   // parti : rien à poser, et rien à attendre

                if (_objects[target.ObjectIndex] is not { } found || DrawReadiness.IsReady(found) is false)
                    return false;

                _extras.Apply(found, extras, change);
                Remember(peer, applied => applied with { ExtrasTouched = true });
                return true;
            }).ConfigureAwait(false);

            if (done)
                return;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        _log.Debug("Extras non posés : le personnage n'a pas fini de se charger en dix secondes.");
    }
```

- [ ] **Step 2: Les retirer avec le reste**

Dans `RemoveAsync`, à l'intérieur de `RunOnFrameworkThread`, juste avant la ligne du relâchement Glamourer :

```csharp
            if (stillThere && applied.ExtrasTouched && _objects[applied.Object!.Value.ObjectIndex] is { } found)
                Try(() => _extras.Clear(found), "retrait des extras");
```

- [ ] **Step 3: Réappliquer quand un plugin redémarre**

Dans `Plugin.cs`, passer `_extras` au constructeur de `RemoteApplicator`, juste après `glamourer`. `_extras` doit donc être construit avant `_applicator` : déplacer sa ligne de construction plus haut si nécessaire. Puis, après `_extras.Changed += ...` :

```csharp
        // Honorific, Moodles ou PetNicknames qui redémarrent ont oublié ce
        // que nous avions posé chez eux : on repose chaque pair affiché.
        _extras.Ready += () =>
        {
            foreach (var status in _engine?.Statuses ?? [])
            {
                if (status.Applied)
                    _engine?.Reapply(status.Peer);
            }
        };
```

- [ ] **Step 4: Compiler, tester, déployer**

Run: `dotnet build Linkpearl/Linkpearl.csproj -c Release && dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj && ./scripts/deploy-plugin-dev.sh`
Expected: succès sans warning, tests verts, déploiement effectué.

- [ ] **Step 5: Commit**

```bash
git add Linkpearl/Integration/RemoteApplicator.cs Linkpearl/Plugin.cs
git commit -m "feat(integration): poser et retirer les extras d'un pair, et les reposer quand un plugin redémarre"
```

---

### Task 10: Documentation et essais en jeu

**Files:**
- Create: `docs/essais-integrations.md`
- Modify: `docs/reprise.md`

- [ ] **Step 1: Écrire la liste d'essais**

`docs/essais-integrations.md` :

```markdown
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
```

- [ ] **Step 2: Mettre à jour `docs/reprise.md`**

Dans « Ce qui vient ensuite », faire suivre le point 1 de : « Fait le 23 septembre, voir `superpowers/specs/2026-09-23-integrations-design.md` et `essais-integrations.md`. » Dans « Ce qui reste de mémoire », ajouter :

```markdown
- **SimpleHeels garde un décalage reçu** jusqu'à ce qu'on le désenregistre, ce
  qui exige un personnage visible : un pair retiré hors de vue garde son
  décalage chez nous jusqu'au rechargement de SimpleHeels.
```

Dans « Pièges appris à la dure », ajouter :

```markdown
- Moodles transporte « Nom@Monde » et des GUID identiques d'un personnage à
  l'autre ; PetNicknames transporte nom, monde et ContentId. Les deux sont
  nettoyés à l'envoi et vérifiés à la réception.
```

- [ ] **Step 3: Commit**

```bash
git add docs/reprise.md docs/essais-integrations.md
git commit -m "docs: essais en jeu des intégrations, et reprise à jour"
```
