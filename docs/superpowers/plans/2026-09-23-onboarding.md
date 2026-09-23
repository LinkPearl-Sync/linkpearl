# Présentation au premier usage : plan d'implémentation

> **Pour les agents :** sous-skill requis : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans, tâche par tâche. Les étapes sont des cases à cocher (`- [ ]`).

**But :** une présentation illustrée en six écrans au premier chargement, qui fait choisir le dossier et le quota du cache, avec un cache qui tient enfin son quota et qui bloque le plugin si son dossier disparaît.

**Architecture :** toute la décision sur le cache (attendre la présentation, ouvrir, bloquer, rechoisir, quota) vit dans le noyau (`Core/Cache/CacheKeeper`), testée sous Linux. Le plugin donne à `LocalAppearance` et `RemoteApplicator` un magasin commutable (`SwitchableBlobStore`) auquel `CacheKeeper` attache ou retire le vrai `FileSystemBlobStore`. Le moteur n'existe que cache ouvert, et évince lui-même au-delà du quota en épargnant ce qui est à l'écran. L'interface (présentation, sélecteur de cache, page de blocage) ne fait que lire cet état.

**Stack :** .NET 10, Dalamud.NET.Sdk 15, ImGui (Dalamud.Bindings.ImGui), xUnit.

**Spec :** `docs/superpowers/specs/2026-09-23-onboarding-design.md`. La lire avant chaque tâche.

## Contraintes globales

- `Linkpearl/Core/` ne référence jamais Dalamud (`ArchitectureTests` le vérifie).
- Pas de tiret cadratin (U+2014) nulle part : code, commentaires, textes, commits.
- Commentaires en français, qui disent le *pourquoi*. Textes d'interface en français.
- Aucun chemin local complet ni nom de personnage dans les journaux (`_log`, `Log`). Un chemin peut s'afficher dans l'interface.
- Commits en Conventional Commits, sujet en français. **Jamais** de ligne `Co-Authored-By:` ni `Claude-Session:`, ni de mention « Generated with ».
- Quota : de 10 à 500 Go, pas de 5, défaut 50 Go. 1 Go = 1024³ octets.
- Linkpearl n'écrit que dans `<dossier choisi>/LinkpearlCache`. Le défaut reste `%LocalAppData%\Linkpearl\cache`.
- Un dossier de cache établi qui manque n'est **jamais** recréé par le plugin.
- IPC, `ObjectTable`, fenêtres : thread du framework seulement. Entrées-sorties disque lourdes : pool de threads.
- Commandes :
  - tests du noyau : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
  - build du plugin, sans warning : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
  - essai en jeu : `./scripts/deploy-plugin-dev.sh`

## Points d'attention pour la relecture

1. **Le dossier supprimé pendant une écriture** : un `Directory.CreateDirectory` au moment de publier un blob recréerait la racine. Couvert par un test de la tâche 1.
2. **Deux pertes simultanées** (le sondage et une écriture refusée au même instant) ne doivent lever `Lost` qu'une fois. Couvert par un test de la tâche 4.
3. **Un quota abaissé sous la taille actuelle** doit faire maigrir le cache sans toucher à ce qui est à l'écran ni à notre apparence. Couvert par les tests de la tâche 5.
4. **La suppression de l'ancien cache** ne doit effacer que nos fichiers, jamais un fichier étranger ni le dossier qui le contient. Couvert par un test de la tâche 2.
5. **Rechoisir en cours de jeu le dossier déjà actif** doit annuler le changement en attente, pas proposer l'actif à la suppression. Couvert par un test de la tâche 4.

---

### Tâche 1 : un magasin de blobs qui ne recrée pas sa racine, et dont le quota change à chaud

**Fichiers :**
- Modifier : `Linkpearl/Core/Cache/IBlobStore.cs`
- Modifier : `Linkpearl/Core/Cache/FileSystemBlobStore.cs`
- Créer : `Linkpearl/Core/Cache/RefusedBlobWriters.cs`
- Test : `Linkpearl.Core.Tests/Cache/FileSystemBlobStoreTests.cs`

**Interfaces :**
- Produit : `IBlobStore.NeedsEviction` (bool, défaut `false`), `IBlobStore.EvictionTarget` (long, défaut `TotalBytes`), `FileSystemBlobStore.Root` (string), `FileSystemBlobStore.RootExists` (bool), `FileSystemBlobStore.SetQuota(long bytes)`, `event Action? FileSystemBlobStore.RootLost`, `internal sealed class RefusedBlobWriter(string reason) : IBlobWriter`, `internal sealed class RefusedBlobAssembly(string reason) : IBlobAssembly`.

- [ ] **Étape 1 : écrire les tests qui échouent**

Ajouter à la fin de `FileSystemBlobStoreTests` (avant l'accolade de fermeture de la classe) :

```csharp
    [Fact]
    public async Task Un_cache_dont_le_dossier_a_disparu_refuse_d_ecrire_sans_le_recreer()
    {
        var store = Store();
        var lost = 0;
        store.RootLost += () => lost++;

        Directory.Delete(_root, recursive: true);

        var result = await PutAsync(store, Bytes("après la disparition"));

        Assert.False(result.Accepted);
        Assert.False(Directory.Exists(_root));
        Assert.True(lost > 0);
    }

    [Fact]
    public async Task Un_dossier_disparu_pendant_une_ecriture_n_est_pas_recree_a_la_publication()
    {
        var store = Store();
        var content = Bytes("écrit pendant qu'on vide le disque");

        await using var writer = await store.BeginWriteAsync(BlobHash.OfContent(content), content.Length, default);
        await writer.WriteAsync(content, default);

        // Sous Linux, un dossier se supprime même avec un fichier ouvert dedans :
        // c'est le cas le plus défavorable, celui où la publication arrive après.
        Directory.Delete(_root, recursive: true);

        var result = await writer.CommitAsync(default);

        Assert.False(result.Accepted);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Un_quota_abaisse_a_chaud_demande_une_eviction()
    {
        var store = Store();
        await PutAsync(store, new byte[100]);

        Assert.False(store.NeedsEviction);

        store.SetQuota(50);

        Assert.True(store.NeedsEviction);
        Assert.Equal((long)(50 * new CacheSettings().LowWatermark), store.EvictionTarget);
    }

    [Fact]
    public async Task Un_quota_abaisse_a_chaud_refuse_un_blob_plus_gros_que_lui()
    {
        var store = Store();
        store.SetQuota(10);

        var result = await PutAsync(store, new byte[20]);

        Assert.False(result.Accepted);
    }
```

- [ ] **Étape 2 : vérifier qu'ils échouent**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~FileSystemBlobStoreTests`
Attendu : échec de compilation, `RootLost` et `SetQuota` n'existent pas.

- [ ] **Étape 3 : sortir les écritures refusées dans leur propre fichier**

Créer `Linkpearl/Core/Cache/RefusedBlobWriters.cs` :

```csharp
namespace Linkpearl.Core.Cache;

/// <summary>Écriture refusée d'avance, pour que l'appelant n'ait qu'un chemin à gérer.</summary>
/// <remarks>
/// Partagée entre le magasin sur disque et le magasin commutable : un refus
/// d'espace, de quota ou de cache absent se lit de la même façon au commit.
/// </remarks>
internal sealed class RefusedBlobWriter(string reason) : IBlobWriter
{
    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
        => ValueTask.FromResult(BlobCommitResult.Refused(reason));

    public void Abort() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Assemblage refusé d'avance, pendant de <see cref="RefusedBlobWriter"/>.</summary>
internal sealed class RefusedBlobAssembly(string reason) : IBlobAssembly
{
    public ValueTask WriteAtAsync(long offset, ReadOnlyMemory<byte> data, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<BlobCommitResult> CommitAsync(CancellationToken ct)
        => ValueTask.FromResult(BlobCommitResult.Refused(reason));

    public void Abort() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Dans `FileSystemBlobStore.cs`, supprimer les classes imbriquées `RefusedWriter` et `RefusedAssembly`, et remplacer leurs usages par `RefusedBlobWriter` et `RefusedBlobAssembly`.

- [ ] **Étape 4 : ajouter les deux membres par défaut à `IBlobStore`**

Dans `IBlobStore.cs`, après `Task EvictToAsync(...)` :

```csharp
    /// <summary>Vrai quand le cache dépasse son quota.</summary>
    /// <remarks>
    /// Par défaut faux : les doublures de test n'ont pas de quota, et n'ont pas
    /// à en inventer un.
    /// </remarks>
    bool NeedsEviction => false;

    /// <summary>La taille à laquelle une éviction doit redescendre.</summary>
    long EvictionTarget => TotalBytes;
```

`FileSystemBlobStore` a déjà deux membres publics de ces noms : ils implémentent l'interface tels quels.

- [ ] **Étape 5 : racine qui ne renaît pas, quota à chaud**

Dans `FileSystemBlobStore` :

1. Remplacer `private readonly CacheSettings _settings;` par :

```csharp
    /// <summary>Remplacé en bloc par <see cref="SetQuota"/>, lu sans verrou.</summary>
    private volatile CacheSettings _settings;
```

2. Après la propriété `IndexPath`, ajouter :

```csharp
    /// <summary>Le dossier du cache, tel qu'il a été ouvert.</summary>
    public string Root => _root;

    /// <summary>Faux si quelqu'un a supprimé le dossier depuis l'ouverture.</summary>
    public bool RootExists => Directory.Exists(_root);

    /// <summary>
    /// Levé quand une écriture trouve le dossier disparu.
    /// </summary>
    /// <remarks>
    /// Le sondage périodique finirait par le voir ; ceci le dit dès qu'un
    /// transfert bute dessus. Peut être levé plusieurs fois, depuis n'importe
    /// quel fil : l'abonné doit être idempotent.
    /// </remarks>
    public event Action? RootLost;

    /// <summary>Change le quota sans rouvrir le cache.</summary>
    /// <remarks>
    /// L'éviction qui suit est l'affaire du moteur, qui sait ce qui est à
    /// l'écran et ne doit pas partir.
    /// </remarks>
    public void SetQuota(long bytes) => _settings = _settings with { QuotaBytes = bytes };

    private const string RootMissing = "dossier du cache introuvable";

    /// <summary>
    /// Vrai si la racine manque, après l'avoir signalé.
    /// </summary>
    /// <remarks>
    /// Jamais de <c>Directory.CreateDirectory</c> de la racine en dehors du
    /// constructeur : un dossier qui a disparu a peut-être été vidé exprès, ou
    /// était sur un disque débranché.
    /// </remarks>
    private bool SignalIfRootMissing()
    {
        if (RootExists)
            return false;

        RootLost?.Invoke();
        return true;
    }
```

3. En tête de `BeginWriteAsync` :

```csharp
        if (SignalIfRootMissing())
            return Task.FromResult<IBlobWriter>(new RefusedBlobWriter(RootMissing));
```

   et en tête de `BeginAssemblyAsync` :

```csharp
        if (SignalIfRootMissing())
            return Task.FromResult<IBlobAssembly>(new RefusedBlobAssembly(RootMissing));
```

4. Dans `BlobAssembly.CommitAsync` **et** dans `BlobWriter.CommitAsync`, juste avant la ligne `var destination = store.PathFor(expected);`, insérer :

```csharp
            // Le dossier a pu disparaître pendant le transfert : le recréer
            // ici, c'est remplir un disque que l'utilisateur vient de vider.
            if (store.SignalIfRootMissing())
            {
                Discard();
                return BlobCommitResult.Refused(RootMissing);
            }
```

- [ ] **Étape 6 : vérifier que tout passe**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert, dont les quatre nouveaux tests.

- [ ] **Étape 7 : commit**

```bash
git add Linkpearl/Core/Cache Linkpearl.Core.Tests/Cache/FileSystemBlobStoreTests.cs
git commit -m "feat(cache): ne jamais recréer un dossier de cache disparu, quota modifiable à chaud"
```

---

### Tâche 2 : l'emplacement du cache, sa vérification, et la suppression prudente d'un ancien

**Fichiers :**
- Créer : `Linkpearl/Core/Cache/CacheLocation.cs`
- Test : `Linkpearl.Core.Tests/Cache/CacheLocationTests.cs`

**Interfaces :**
- Produit :
  - `public sealed record CachePreparation(string? Root, string? Error)`
  - `public static class CacheLocation` avec `const string FolderName = "LinkpearlCache"`, `string Resolve(string configured, string defaultRoot)`, `string ForChosen(string chosen)`, `CachePreparation Prepare(string chosen)`, `long Measure(string root)`, `int DeleteOwned(string root)`.

- [ ] **Étape 1 : écrire les tests qui échouent**

Créer `Linkpearl.Core.Tests/Cache/CacheLocationTests.cs` :

```csharp
using Linkpearl.Core.Cache;
using Xunit;

namespace Linkpearl.Core.Tests.Cache;

/// <summary>
/// Le dossier du cache vient d'un choix de l'utilisateur, et sa suppression
/// touche à son disque. Ces tests portent sur ce qu'on ne doit jamais effacer.
/// </summary>
public sealed class CacheLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "linkpearl-emplacement-" + Guid.NewGuid().ToString("N"));

    public CacheLocationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly string Hex = new('a', 64);

    [Fact]
    public void Un_reglage_vide_designe_le_dossier_par_defaut()
    {
        Assert.Equal("/defaut/cache", CacheLocation.Resolve("", "/defaut/cache"));
        Assert.Equal("/ailleurs", CacheLocation.Resolve("/ailleurs", "/defaut/cache"));
    }

    [Fact]
    public void Le_cache_va_dans_un_sous_dossier_a_nous()
    {
        Assert.Equal(Path.Combine(_root, "LinkpearlCache"), CacheLocation.ForChosen(_root));
    }

    [Fact]
    public void Choisir_le_sous_dossier_lui_meme_ne_l_imbrique_pas()
    {
        var ours = Path.Combine(_root, "LinkpearlCache");

        Assert.Equal(ours, CacheLocation.ForChosen(ours));
        Assert.Equal(ours, CacheLocation.ForChosen(ours + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Un_dossier_utilisable_est_prepare_sans_temoin_residuel()
    {
        var prepared = CacheLocation.Prepare(_root);

        Assert.Null(prepared.Error);
        Assert.Equal(Path.Combine(_root, "LinkpearlCache"), prepared.Root);
        Assert.True(Directory.Exists(prepared.Root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(prepared.Root!));
    }

    [Fact]
    public void Un_chemin_relatif_est_refuse()
    {
        var prepared = CacheLocation.Prepare("relatif/cache");

        Assert.Null(prepared.Root);
        Assert.NotNull(prepared.Error);
    }

    [Fact]
    public void Un_emplacement_qui_est_un_fichier_est_refuse()
    {
        var file = Path.Combine(_root, "un-fichier");
        File.WriteAllText(file, "pas un dossier");

        var prepared = CacheLocation.Prepare(file);

        Assert.Null(prepared.Root);
        Assert.NotNull(prepared.Error);
    }

    [Fact]
    public void La_mesure_compte_tout_ce_qui_est_dans_le_dossier()
    {
        Directory.CreateDirectory(Path.Combine(_root, "blobs", "aa"));
        File.WriteAllBytes(Path.Combine(_root, "blobs", "aa", Hex), new byte[30]);
        File.WriteAllBytes(Path.Combine(_root, "cache.index"), new byte[12]);

        Assert.Equal(42, CacheLocation.Measure(_root));
        Assert.Equal(0, CacheLocation.Measure(Path.Combine(_root, "absent")));
    }

    [Fact]
    public void La_suppression_n_efface_que_nos_fichiers()
    {
        var leaf = Path.Combine(_root, "blobs", "aa", "aa");
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(Path.Combine(_root, "incoming"));

        File.WriteAllBytes(Path.Combine(leaf, Hex), new byte[10]);
        File.WriteAllBytes(Path.Combine(_root, "incoming", "0123.part"), new byte[10]);
        File.WriteAllText(Path.Combine(_root, "cache.index"), "");
        File.WriteAllText(Path.Combine(_root, "vacances.jpg"), "à l'utilisateur");
        File.WriteAllText(Path.Combine(leaf, "notes.txt"), "à l'utilisateur aussi");

        var deleted = CacheLocation.DeleteOwned(_root);

        Assert.Equal(3, deleted);
        Assert.True(File.Exists(Path.Combine(_root, "vacances.jpg")));
        Assert.True(File.Exists(Path.Combine(leaf, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(leaf, Hex)));
        Assert.False(Directory.Exists(Path.Combine(_root, "incoming")));
    }

    [Fact]
    public void Un_cache_entierement_a_nous_disparait_avec_son_dossier()
    {
        var cache = Path.Combine(_root, "LinkpearlCache");
        var leaf = Path.Combine(cache, "blobs", "aa", "aa");
        Directory.CreateDirectory(leaf);
        Directory.CreateDirectory(Path.Combine(cache, "incoming"));
        File.WriteAllBytes(Path.Combine(leaf, Hex), new byte[10]);
        File.WriteAllText(Path.Combine(cache, "cache.index"), "");

        CacheLocation.DeleteOwned(cache);

        Assert.False(Directory.Exists(cache));
        Assert.True(Directory.Exists(_root));
    }
}
```

- [ ] **Étape 2 : vérifier qu'ils échouent**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~CacheLocationTests`
Attendu : échec de compilation, `CacheLocation` n'existe pas.

- [ ] **Étape 3 : implémenter**

Créer `Linkpearl/Core/Cache/CacheLocation.cs` :

```csharp
namespace Linkpearl.Core.Cache;

/// <summary>Un dossier prêt à recevoir le cache, ou la raison du refus.</summary>
public sealed record CachePreparation(string? Root, string? Error);

/// <summary>
/// Où vit le cache, et comment on s'en débarrasse.
/// </summary>
/// <remarks>
/// Linkpearl n'écrit jamais directement dans le dossier choisi : il y crée
/// <see cref="FolderName"/>. Choisir <c>D:\Jeux</c> ne mélange donc rien aux
/// fichiers de l'utilisateur, et supprimer le cache ne touche qu'à ce qui est
/// à nous.
/// </remarks>
public static class CacheLocation
{
    public const string FolderName = "LinkpearlCache";

    private const string Probe = ".linkpearl-probe";

    /// <summary>Le dossier effectif : le réglage, ou le défaut s'il est vide.</summary>
    public static string Resolve(string configured, string defaultRoot)
        => configured is "" ? defaultRoot : configured;

    /// <summary>Le dossier du cache pour un dossier choisi.</summary>
    /// <remarks>
    /// Rechoisir <see cref="FolderName"/> lui-même, par exemple après l'avoir
    /// recréé, ne doit pas donner <c>LinkpearlCache/LinkpearlCache</c>.
    /// </remarks>
    public static string ForChosen(string chosen)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));

        return string.Equals(Path.GetFileName(full), FolderName, StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, FolderName);
    }

    /// <summary>
    /// Crée le sous-dossier et vérifie qu'on peut y écrire.
    /// </summary>
    /// <remarks>
    /// Un fichier témoin écrit puis effacé : un disque en lecture seule ou un
    /// dossier protégé se découvrent ici, et non au premier transfert.
    /// </remarks>
    public static CachePreparation Prepare(string chosen)
    {
        if (string.IsNullOrWhiteSpace(chosen) || Path.IsPathFullyQualified(chosen) is false)
            return new(null, "choisissez un dossier complet, lecteur compris.");

        try
        {
            var root = ForChosen(chosen);
            Directory.CreateDirectory(root);

            var probe = Path.Combine(root, Probe);
            File.WriteAllBytes(probe, [1]);
            File.Delete(probe);

            return new(root, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                     or NotSupportedException or ArgumentException)
        {
            return new(null, $"ce dossier n'est pas utilisable : {e.Message}");
        }
    }

    /// <summary>La place qu'occupe un dossier, zéro s'il n'existe pas.</summary>
    /// <remarks>Parcourt tout l'arbre : à appeler depuis le pool de threads.</remarks>
    public static long Measure(string root)
    {
        if (Directory.Exists(root) is false)
            return 0;

        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(file => file.Length);
    }

    /// <summary>
    /// Efface un ancien cache, fichier par fichier.
    /// </summary>
    /// <remarks>
    /// Jamais de suppression récursive d'un dossier : seulement les blobs dont
    /// le nom est un hash, les écritures interrompues et l'index. Un fichier
    /// étranger y survit toujours, et le dossier qui le contient avec lui.
    /// Parcourt tout l'arbre : à appeler depuis le pool de threads.
    /// </remarks>
    /// <returns>Le nombre de fichiers effacés.</returns>
    public static int DeleteOwned(string root)
    {
        if (Directory.Exists(root) is false)
            return 0;

        var deleted = 0;
        var blobs = Path.Combine(root, "blobs");
        var incoming = Path.Combine(root, "incoming");

        if (Directory.Exists(blobs))
        {
            foreach (var file in Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories).ToList())
            {
                if (BlobHash.TryParseHex(Path.GetFileName(file), out _) && TryDelete(file))
                    deleted++;
            }
        }

        if (Directory.Exists(incoming))
        {
            foreach (var file in Directory.EnumerateFiles(incoming, "*.part").ToList())
            {
                if (TryDelete(file))
                    deleted++;
            }
        }

        foreach (var name in new[] { "cache.index", "cache.index.part" })
        {
            var file = Path.Combine(root, name);

            if (File.Exists(file) && TryDelete(file))
                deleted++;
        }

        if (Directory.Exists(blobs))
            RemoveEmptyTree(blobs);

        RemoveIfEmpty(incoming);
        RemoveIfEmpty(root);

        return deleted;
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Un blob en cours de lecture : il restera, et le dossier avec lui.
            return false;
        }
    }

    /// <summary>Les dossiers sous <c>blobs</c> sont les deux niveaux de hash : à nous.</summary>
    private static void RemoveEmptyTree(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory).ToList())
            RemoveEmptyTree(child);

        RemoveIfEmpty(directory);
    }

    private static void RemoveIfEmpty(string directory)
    {
        if (Directory.Exists(directory) is false || Directory.EnumerateFileSystemEntries(directory).Any())
            return;

        try
        {
            Directory.Delete(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Vide mais verrouillé : il restera, sans rien coûter.
        }
    }
}
```

- [ ] **Étape 4 : vérifier que tout passe**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert.

- [ ] **Étape 5 : commit**

```bash
git add Linkpearl/Core/Cache/CacheLocation.cs Linkpearl.Core.Tests/Cache/CacheLocationTests.cs
git commit -m "feat(cache): emplacement du cache dans un sous-dossier à nous, et suppression prudente"
```

---

### Tâche 3 : le magasin commutable

**Fichiers :**
- Créer : `Linkpearl/Core/Cache/SwitchableBlobStore.cs`
- Test : `Linkpearl.Core.Tests/Cache/SwitchableBlobStoreTests.cs`

**Interfaces :**
- Consomme : `FileSystemBlobStore` (tâche 1), `RefusedBlobWriter`, `RefusedBlobAssembly` (tâche 1).
- Produit : `public sealed class SwitchableBlobStore : IBlobStore` avec `FileSystemBlobStore? Current`, `bool IsAvailable`, `void Attach(FileSystemBlobStore store)`, `FileSystemBlobStore? Detach()`.

- [ ] **Étape 1 : écrire les tests qui échouent**

Créer `Linkpearl.Core.Tests/Cache/SwitchableBlobStoreTests.cs` :

```csharp
using System.Text;
using Linkpearl.Core.Cache;
using Xunit;

namespace Linkpearl.Core.Tests.Cache;

public sealed class SwitchableBlobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "linkpearl-commutable-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeClock : Linkpearl.Core.Abstractions.IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    }

    private FileSystemBlobStore Disk() => new(_root, new CacheSettings(), new FakeClock(), _ => long.MaxValue);

    private static async Task<BlobCommitResult> PutAsync(IBlobStore store, byte[] content)
    {
        await using var writer = await store.BeginWriteAsync(BlobHash.OfContent(content), content.Length, default);
        await writer.WriteAsync(content, default);
        return await writer.CommitAsync(default);
    }

    [Fact]
    public async Task Sans_cache_attache_tout_est_refuse_et_rien_n_est_ecrit()
    {
        var store = new SwitchableBlobStore();
        var content = Encoding.UTF8.GetBytes("rien ne doit arriver sur le disque");

        Assert.False(store.IsAvailable);
        Assert.False((await PutAsync(store, content)).Accepted);
        Assert.False(store.TryGetSize(BlobHash.OfContent(content), out _));
        Assert.True(store.IsReadOnly);
        Assert.False(store.NeedsEviction);
        Assert.Equal(0, store.TotalBytes);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Un_cache_attache_recoit_les_ecritures()
    {
        var store = new SwitchableBlobStore();
        var disk = Disk();
        store.Attach(disk);

        var content = Encoding.UTF8.GetBytes("écrit à travers le commutateur");

        Assert.True((await PutAsync(store, content)).Accepted);
        Assert.True(disk.TryGetSize(BlobHash.OfContent(content), out _));
        Assert.Equal(disk.PathFor(BlobHash.OfContent(content)), store.PathFor(BlobHash.OfContent(content)));
    }

    [Fact]
    public async Task Un_cache_detache_ne_recoit_plus_rien()
    {
        var store = new SwitchableBlobStore();
        var disk = Disk();
        store.Attach(disk);

        Assert.Same(disk, store.Detach());
        Assert.Null(store.Detach());

        var content = Encoding.UTF8.GetBytes("après le détachement");

        Assert.False((await PutAsync(store, content)).Accepted);
        Assert.False(disk.TryGetSize(BlobHash.OfContent(content), out _));
    }

    [Fact]
    public async Task Sans_cache_une_lecture_echoue_comme_un_blob_absent()
    {
        var store = new SwitchableBlobStore();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => store.OpenReadAsync(BlobHash.OfContent([1, 2, 3]), default));
    }
}
```

- [ ] **Étape 2 : vérifier qu'ils échouent**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~SwitchableBlobStoreTests`
Attendu : échec de compilation.

- [ ] **Étape 3 : implémenter**

Créer `Linkpearl/Core/Cache/SwitchableBlobStore.cs` :

```csharp
namespace Linkpearl.Core.Cache;

/// <summary>
/// Un cache dont le dossier peut arriver tard, ou partir.
/// </summary>
/// <remarks>
/// L'apparence locale et l'applicateur sont construits au chargement, mais le
/// vrai cache n'existe qu'après la présentation, et disparaît si son dossier
/// est supprimé. Plutôt que de reconstruire ces deux-là, on leur donne ce
/// commutateur : sans cache attaché, tout est refusé comme par un disque
/// plein, et rien n'arrive sur le disque.
/// </remarks>
public sealed class SwitchableBlobStore : IBlobStore
{
    private const string Unavailable = "cache indisponible";

    private FileSystemBlobStore? _inner;

    public FileSystemBlobStore? Current => Volatile.Read(ref _inner);

    public bool IsAvailable => Current is not null;

    public void Attach(FileSystemBlobStore store) => Volatile.Write(ref _inner, store);

    /// <summary>Retire le cache, et le rend à qui doit s'en désabonner.</summary>
    public FileSystemBlobStore? Detach() => Interlocked.Exchange(ref _inner, null);

    public bool TryGetSize(BlobHash hash, out long size)
    {
        if (Current is { } store)
            return store.TryGetSize(hash, out size);

        size = 0;
        return false;
    }

    public Task<Stream> OpenReadAsync(BlobHash hash, CancellationToken ct)
        => Current is { } store
            ? store.OpenReadAsync(hash, ct)
            : throw new FileNotFoundException($"{Unavailable} : {hash}");

    public Task<IBlobWriter> BeginWriteAsync(BlobHash expected, long expectedSize, CancellationToken ct)
        => Current is { } store
            ? store.BeginWriteAsync(expected, expectedSize, ct)
            : Task.FromResult<IBlobWriter>(new RefusedBlobWriter(Unavailable));

    public Task<IBlobAssembly> BeginAssemblyAsync(BlobHash expected, long expectedSize, CancellationToken ct)
        => Current is { } store
            ? store.BeginAssemblyAsync(expected, expectedSize, ct)
            : Task.FromResult<IBlobAssembly>(new RefusedBlobAssembly(Unavailable));

    /// <remarks>
    /// Seul le moteur demande un chemin, pour poser une apparence, et il n'existe
    /// que cache attaché : l'appel sans cache est une faute de programmation.
    /// </remarks>
    public string PathFor(BlobHash hash)
        => (Current ?? throw new InvalidOperationException(Unavailable)).PathFor(hash);

    public long TotalBytes => Current?.TotalBytes ?? 0;

    public int Count => Current?.Count ?? 0;

    public bool IsReadOnly => Current?.IsReadOnly ?? true;

    public void Touch(BlobHash hash) => Current?.Touch(hash);

    public Task EvictToAsync(long targetBytes, IReadOnlySet<BlobHash> pinned, CancellationToken ct)
        => Current?.EvictToAsync(targetBytes, pinned, ct) ?? Task.CompletedTask;

    public bool NeedsEviction => Current?.NeedsEviction ?? false;

    public long EvictionTarget => Current?.EvictionTarget ?? 0;
}
```

- [ ] **Étape 4 : vérifier que tout passe**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert.

- [ ] **Étape 5 : commit**

```bash
git add Linkpearl/Core/Cache/SwitchableBlobStore.cs Linkpearl.Core.Tests/Cache/SwitchableBlobStoreTests.cs
git commit -m "feat(cache): magasin commutable, pour un cache qui arrive tard ou disparaît"
```

---

### Tâche 4 : le gardien du cache

**Fichiers :**
- Créer : `Linkpearl/Core/Cache/ICacheConfiguration.cs`
- Créer : `Linkpearl/Core/Cache/CacheKeeper.cs`
- Test : `Linkpearl.Core.Tests/Cache/CacheKeeperTests.cs`

**Interfaces :**
- Consomme : `FileSystemBlobStore.Root`, `.RootExists`, `.RootLost`, `.SetQuota` (tâche 1) ; `CacheLocation` (tâche 2) ; `SwitchableBlobStore` (tâche 3).
- Produit :
  - `public interface ICacheConfiguration` : `string CacheDirectory`, `long CacheQuotaBytes`, `bool OnboardingSeen`, `bool CacheEstablished`, `string PreviousCacheDirectory` (tous `{ get; set; }`), `void Save()`.
  - `public enum CacheGateState { AwaitingOnboarding, Open, Missing }`
  - `public sealed class CacheKeeper` : constructeur `(ICacheConfiguration configuration, string defaultRoot, IClock clock, Func<string, long> freeSpace, ILogSink log)` ; constantes `GiB`, `MinQuotaGiB = 10`, `MaxQuotaGiB = 500`, `QuotaStepGiB = 5` ; propriétés `SwitchableBlobStore Store`, `CacheGateState State`, `string? LostRoot`, `bool RestartPending`, `long? PreviousBytes`, `string ConfiguredRoot`, `string? ActiveRoot`, `string? PreviousRoot`, `int QuotaGiB` ; événements `Opened`, `Lost` ; méthodes `Start()`, `FinishOnboarding()`, `string? Choose(string chosen)`, `void SetQuota(int gib)`, `long FreeSpace(string path)`, `void Check()`, `void MeasurePrevious()`, `int DeletePrevious()`.

- [ ] **Étape 1 : écrire les tests qui échouent**

Créer `Linkpearl.Core.Tests/Cache/CacheKeeperTests.cs` :

```csharp
using Linkpearl.Core.Cache;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Cache;

/// <summary>
/// Le gardien décide quand le cache existe. La règle qui compte le plus : un
/// dossier établi qui disparaît n'est jamais recréé, le plugin s'arrête et
/// attend un nouveau choix.
/// </summary>
public sealed class CacheKeeperTests : IDisposable
{
    private sealed class FakeConfiguration : ICacheConfiguration
    {
        public string CacheDirectory { get; set; } = "";
        public long CacheQuotaBytes { get; set; } = 50L * 1024 * 1024 * 1024;
        public bool OnboardingSeen { get; set; }
        public bool CacheEstablished { get; set; }
        public string PreviousCacheDirectory { get; set; } = "";
        public int Saves { get; private set; }
        public void Save() => Saves++;
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "linkpearl-gardien-" + Guid.NewGuid().ToString("N"));

    private readonly FakeConfiguration _configuration = new();

    public CacheKeeperTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string DefaultRoot => Path.Combine(_root, "defaut", "cache");

    private CacheKeeper Keeper()
        => new(_configuration, DefaultRoot, new MovableClock(), _ => long.MaxValue, new SilentLog());

    private string Chosen(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Avant_la_presentation_rien_n_existe_sur_le_disque()
    {
        var keeper = Keeper();
        keeper.Start();

        Assert.Equal(CacheGateState.AwaitingOnboarding, keeper.State);
        Assert.False(keeper.Store.IsAvailable);
        Assert.False(Directory.Exists(DefaultRoot));
    }

    [Fact]
    public void Fermer_la_presentation_ouvre_le_cache_par_defaut()
    {
        var keeper = Keeper();
        var opened = 0;
        keeper.Opened += () => opened++;
        keeper.Start();

        keeper.FinishOnboarding();

        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.Equal(DefaultRoot, keeper.ActiveRoot);
        Assert.True(_configuration.OnboardingSeen);
        Assert.True(_configuration.CacheEstablished);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void Le_dossier_choisi_pendant_la_presentation_sert_des_l_ouverture()
    {
        var keeper = Keeper();
        keeper.Start();

        Assert.Null(keeper.Choose(Chosen("jeux")));
        Assert.False(keeper.Store.IsAvailable);

        keeper.FinishOnboarding();

        Assert.Equal(Path.Combine(_root, "jeux", "LinkpearlCache"), keeper.ActiveRoot);
        Assert.False(Directory.Exists(DefaultRoot));
    }

    [Fact]
    public void Un_cache_etabli_dont_le_dossier_manque_bloque_sans_etre_recree()
    {
        _configuration.OnboardingSeen = true;
        _configuration.CacheEstablished = true;
        _configuration.CacheDirectory = Path.Combine(_root, "disparu", "LinkpearlCache");

        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(_configuration.CacheDirectory, keeper.LostRoot);
        Assert.False(Directory.Exists(_configuration.CacheDirectory));
        Assert.False(keeper.Store.IsAvailable);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void Le_tout_premier_cache_cree_son_dossier()
    {
        _configuration.OnboardingSeen = true;

        var keeper = Keeper();
        keeper.Start();

        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.True(Directory.Exists(DefaultRoot));
    }

    [Fact]
    public void Un_dossier_supprime_en_cours_de_jeu_est_vu_au_sondage()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        Directory.Delete(DefaultRoot, recursive: true);
        keeper.Check();
        keeper.Check();

        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(DefaultRoot, keeper.LostRoot);
        Assert.False(keeper.Store.IsAvailable);
        Assert.False(Directory.Exists(DefaultRoot));
        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task Une_ecriture_qui_bute_sur_le_dossier_disparu_ne_perd_le_cache_qu_une_fois()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        var disk = keeper.Store.Current!;
        Directory.Delete(DefaultRoot, recursive: true);

        // Le magasin détaché reste entre les mains d'un transfert en cours : ses
        // refus ne doivent pas relever la perte.
        await disk.BeginWriteAsync(BlobHash.OfContent([1]), 1, default);
        keeper.Check();
        await disk.BeginWriteAsync(BlobHash.OfContent([2]), 1, default);

        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void Rechoisir_un_dossier_apres_la_perte_rouvre_sans_rechargement()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        var opened = 0;
        keeper.Opened += () => opened++;
        keeper.Start();
        Directory.Delete(DefaultRoot, recursive: true);
        keeper.Check();

        Assert.Null(keeper.Choose(Chosen("nouveau")));

        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.Equal(Path.Combine(_root, "nouveau", "LinkpearlCache"), keeper.ActiveRoot);
        Assert.Null(keeper.LostRoot);
        Assert.Equal(2, opened);
    }

    [Fact]
    public void Changer_de_dossier_cache_ouvert_attend_le_prochain_chargement()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        keeper.Start();

        Assert.Null(keeper.Choose(Chosen("ailleurs")));

        Assert.True(keeper.RestartPending);
        Assert.Equal(DefaultRoot, keeper.ActiveRoot);
        Assert.Equal(Path.Combine(_root, "ailleurs", "LinkpearlCache"), _configuration.CacheDirectory);
        Assert.Equal(DefaultRoot, _configuration.PreviousCacheDirectory);

        // L'ancien est encore celui qu'on utilise : pas question de le proposer
        // à la suppression avant le rechargement.
        Assert.Null(keeper.PreviousRoot);
    }

    [Fact]
    public void Rechoisir_le_dossier_actif_annule_le_changement_en_attente()
    {
        _configuration.OnboardingSeen = true;
        _configuration.CacheDirectory = Path.Combine(_root, "actif", "LinkpearlCache");
        var keeper = Keeper();
        keeper.Start();

        keeper.Choose(Chosen("ailleurs"));
        Assert.Null(keeper.Choose(Path.Combine(_root, "actif")));

        Assert.False(keeper.RestartPending);
        Assert.Equal(keeper.ActiveRoot, _configuration.CacheDirectory);
        Assert.Equal("", _configuration.PreviousCacheDirectory);
    }

    [Fact]
    public void Au_chargement_suivant_l_ancien_cache_se_propose_et_se_supprime()
    {
        var old = Path.Combine(_root, "ancien", "LinkpearlCache");
        var leaf = Path.Combine(old, "blobs", "aa", "aa");
        Directory.CreateDirectory(leaf);
        File.WriteAllBytes(Path.Combine(leaf, new string('a', 64)), new byte[40]);
        File.WriteAllText(Path.Combine(old, "a-garder.txt"), "étranger");

        _configuration.OnboardingSeen = true;
        _configuration.PreviousCacheDirectory = old;
        var keeper = Keeper();
        keeper.Start();

        Assert.Equal(old, keeper.PreviousRoot);

        keeper.MeasurePrevious();
        Assert.Equal(40 + "étranger"u8.Length, keeper.PreviousBytes);

        Assert.Equal(1, keeper.DeletePrevious());
        Assert.True(File.Exists(Path.Combine(old, "a-garder.txt")));
        Assert.Null(keeper.PreviousRoot);
        Assert.Equal("", _configuration.PreviousCacheDirectory);
    }

    [Theory]
    [InlineData(3, 10)]
    [InlineData(80, 80)]
    [InlineData(9000, 500)]
    public void Le_quota_reste_dans_ses_bornes_et_s_applique_au_cache_ouvert(int asked, int kept)
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        keeper.Start();

        keeper.SetQuota(asked);

        Assert.Equal(kept, keeper.QuotaGiB);
        Assert.Equal(kept * CacheKeeper.GiB, _configuration.CacheQuotaBytes);
    }

    [Fact]
    public void Un_dossier_inutilisable_est_refuse_et_rien_ne_change()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        keeper.Start();

        var error = keeper.Choose("relatif");

        Assert.NotNull(error);
        Assert.False(keeper.RestartPending);
        Assert.Equal("", _configuration.CacheDirectory);
    }
}
```

`MovableClock` et `SilentLog` existent déjà dans le projet de tests, espace `Linkpearl.Core.Tests.Sync`.

- [ ] **Étape 2 : vérifier qu'ils échouent**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter FullyQualifiedName~CacheKeeperTests`
Attendu : échec de compilation.

- [ ] **Étape 3 : l'interface de configuration**

Créer `Linkpearl/Core/Cache/ICacheConfiguration.cs` :

```csharp
namespace Linkpearl.Core.Cache;

/// <summary>
/// Ce que le gardien du cache lit et écrit dans la configuration du plugin.
/// </summary>
/// <remarks>
/// Une interface du noyau plutôt que la configuration elle-même, qui dépend de
/// Dalamud : c'est ce qui permet de tester le gardien sous Linux.
/// </remarks>
public interface ICacheConfiguration
{
    /// <summary>Dossier du cache. Vide pour le défaut sous LOCALAPPDATA.</summary>
    string CacheDirectory { get; set; }

    long CacheQuotaBytes { get; set; }

    /// <summary>La présentation a été fermée une fois : le cache peut exister.</summary>
    bool OnboardingSeen { get; set; }

    /// <summary>Le cache a déjà créé son dossier une fois : sa disparition est un incident.</summary>
    bool CacheEstablished { get; set; }

    /// <summary>L'ancien dossier après un changement, à proposer à la suppression. Vide sinon.</summary>
    string PreviousCacheDirectory { get; set; }

    void Save();
}
```

- [ ] **Étape 4 : le gardien**

Créer `Linkpearl/Core/Cache/CacheKeeper.cs` :

```csharp
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Cache;

/// <summary>Ce que le cache permet en ce moment.</summary>
public enum CacheGateState
{
    /// <summary>La présentation n'a pas encore été fermée : rien ne touche au disque.</summary>
    AwaitingOnboarding,

    Open,

    /// <summary>Le dossier a disparu : tout est arrêté jusqu'à un nouveau choix.</summary>
    Missing,
}

/// <summary>
/// Décide quand le cache existe, où, et ce qui arrive quand il disparaît.
/// </summary>
/// <remarks>
/// Condition posée par l'utilisateur : un dossier de cache qui a disparu n'est
/// jamais recréé. Il a peut-être été vidé exprès, ou était sur un disque
/// débranché. Le plugin s'arrête et demande d'en choisir un autre.
///
/// Les appels peuvent venir de l'interface et de la boucle de synchronisation :
/// l'état est lu sans verrou, et seule la perte, qui peut arriver de deux fils
/// à la fois, en prend un.
/// </remarks>
public sealed class CacheKeeper(
    ICacheConfiguration configuration, string defaultRoot, IClock clock, Func<string, long> freeSpace, ILogSink log)
{
    public const long GiB = 1024L * 1024 * 1024;
    public const int MinQuotaGiB = 10;
    public const int MaxQuotaGiB = 500;
    public const int QuotaStepGiB = 5;

    private readonly object _gate = new();
    private volatile CacheGateState _state = CacheGateState.AwaitingOnboarding;
    private volatile string? _lostRoot;
    private volatile bool _restartPending;
    private long _previousBytes = -1;

    /// <summary>Le cache tel que le voient l'apparence locale, l'applicateur et le moteur.</summary>
    public SwitchableBlobStore Store { get; } = new();

    public CacheGateState State => _state;

    /// <summary>Le dossier disparu, pour le dire à l'utilisateur. Null hors blocage.</summary>
    public string? LostRoot => _lostRoot;

    /// <summary>Un autre dossier a été choisi cache ouvert : il servira au prochain chargement.</summary>
    public bool RestartPending => _restartPending;

    /// <summary>La taille de l'ancien cache, une fois mesurée.</summary>
    public long? PreviousBytes
    {
        get
        {
            var bytes = Interlocked.Read(ref _previousBytes);
            return bytes < 0 ? null : bytes;
        }
    }

    /// <summary>Le dossier que désigne la configuration.</summary>
    public string ConfiguredRoot => CacheLocation.Resolve(configuration.CacheDirectory, defaultRoot);

    /// <summary>Le dossier du cache en service. Null s'il n'y en a pas.</summary>
    public string? ActiveRoot => Store.Current?.Root;

    /// <summary>
    /// L'ancien cache à proposer à la suppression.
    /// </summary>
    /// <remarks>
    /// Seulement après le rechargement : tant que le changement attend, l'ancien
    /// est celui qu'on utilise.
    /// </remarks>
    public string? PreviousRoot
    {
        get
        {
            var previous = configuration.PreviousCacheDirectory;

            if (_restartPending || previous is "" || SamePath(previous, ActiveRoot))
                return null;

            return previous;
        }
    }

    public int QuotaGiB => (int)Math.Clamp(configuration.CacheQuotaBytes / GiB, MinQuotaGiB, MaxQuotaGiB);

    /// <summary>Le cache vient d'être attaché. Levé sur le fil de l'appelant.</summary>
    public event Action? Opened;

    /// <summary>Le cache est bloqué, dossier introuvable. Levé une fois par perte.</summary>
    public event Action? Lost;

    /// <summary>Au chargement du plugin.</summary>
    public void Start()
    {
        if (configuration.OnboardingSeen is false)
        {
            _state = CacheGateState.AwaitingOnboarding;
            return;
        }

        OpenConfigured();
    }

    /// <summary>La présentation vient d'être fermée, par la croix ou par « C'est parti ».</summary>
    public void FinishOnboarding()
    {
        if (configuration.OnboardingSeen)
            return;

        configuration.OnboardingSeen = true;
        configuration.Save();
        OpenConfigured();
    }

    /// <summary>
    /// Un dossier choisi par l'utilisateur.
    /// </summary>
    /// <remarks>
    /// Crée le sous-dossier et y écrit un témoin : à appeler depuis le pool de
    /// threads. Après une perte, ouvre aussi le cache, index compris.
    /// </remarks>
    /// <returns>Null si le choix est retenu, sinon pourquoi il ne l'est pas.</returns>
    public string? Choose(string chosen)
    {
        var prepared = CacheLocation.Prepare(chosen);

        if (prepared.Root is not { } root)
            return prepared.Error;

        switch (_state)
        {
            case CacheGateState.Open:
                if (SamePath(root, ActiveRoot))
                {
                    configuration.PreviousCacheDirectory = "";
                    _restartPending = false;
                }
                else
                {
                    configuration.PreviousCacheDirectory = ActiveRoot ?? "";
                    _restartPending = true;
                }

                configuration.CacheDirectory = root;
                configuration.Save();
                return null;

            case CacheGateState.Missing:
                configuration.CacheDirectory = root;
                configuration.Save();
                OpenConfigured();
                return _state is CacheGateState.Open ? null : "ce dossier n'a pas pu être ouvert.";

            default:
                configuration.CacheDirectory = root;
                configuration.Save();
                return null;
        }
    }

    /// <summary>Le quota, en gigaoctets, borné et appliqué tout de suite.</summary>
    public void SetQuota(int gib)
    {
        var kept = Math.Clamp(gib, MinQuotaGiB, MaxQuotaGiB);

        configuration.CacheQuotaBytes = kept * GiB;
        configuration.Save();
        Store.Current?.SetQuota(kept * GiB);
    }

    /// <summary>L'espace libre du disque qui porte ce chemin, -1 s'il est illisible.</summary>
    public long FreeSpace(string path)
    {
        try
        {
            return freeSpace(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Un lecteur débranché : on n'affiche rien plutôt qu'un chiffre faux.
            return -1;
        }
    }

    /// <summary>Le sondage : le dossier est-il toujours là ?</summary>
    public void Check()
    {
        if (_state is CacheGateState.Open && Store.Current is { RootExists: false })
            Lose();
    }

    /// <summary>Mesure l'ancien cache. Parcourt tout l'arbre : pool de threads.</summary>
    public void MeasurePrevious()
    {
        if (PreviousRoot is { } previous)
            Interlocked.Exchange(ref _previousBytes, CacheLocation.Measure(previous));
    }

    /// <summary>Efface nos fichiers de l'ancien cache, et l'oublie. Pool de threads.</summary>
    /// <returns>Le nombre de fichiers effacés.</returns>
    public int DeletePrevious()
    {
        if (PreviousRoot is not { } previous)
            return 0;

        var deleted = CacheLocation.DeleteOwned(previous);

        configuration.PreviousCacheDirectory = "";
        configuration.Save();
        Interlocked.Exchange(ref _previousBytes, -1);

        log.Info($"ancien cache supprimé : {deleted} fichier(s).");
        return deleted;
    }

    private void OpenConfigured()
    {
        var root = ConfiguredRoot;

        if (configuration.CacheEstablished && Directory.Exists(root) is false)
        {
            MarkMissing(root);
            return;
        }

        FileSystemBlobStore store;

        try
        {
            store = new FileSystemBlobStore(root, new CacheSettings { QuotaBytes = QuotaGiB * GiB }, clock, freeSpace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                     or NotSupportedException or ArgumentException)
        {
            log.Warning("ouverture du cache impossible.", e);
            MarkMissing(root);
            return;
        }

        store.RootLost += Lose;
        Store.Attach(store);

        if (configuration.CacheEstablished is false)
        {
            configuration.CacheEstablished = true;
            configuration.Save();
        }

        _lostRoot = null;
        _state = CacheGateState.Open;
        log.Info("cache ouvert.");
        Opened?.Invoke();
    }

    /// <summary>
    /// Le dossier a disparu : on lâche le cache.
    /// </summary>
    /// <remarks>
    /// Peut arriver du sondage et d'une écriture au même instant : le verrou
    /// garantit une seule perte, donc un seul arrêt et une seule notification.
    /// </remarks>
    private void Lose()
    {
        FileSystemBlobStore? lost;

        lock (_gate)
        {
            if (_state is not CacheGateState.Open)
                return;

            _state = CacheGateState.Missing;
            lost = Store.Detach();
        }

        if (lost is not null)
            lost.RootLost -= Lose;

        MarkMissing(lost?.Root ?? ConfiguredRoot);
    }

    private void MarkMissing(string root)
    {
        _lostRoot = root;
        _state = CacheGateState.Missing;
        log.Warning("dossier du cache introuvable : synchronisation suspendue.");
        Lost?.Invoke();
    }

    private static bool SamePath(string? a, string? b)
        => a is not null && b is not null && string.Equals(
               Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
               StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Étape 5 : vérifier que tout passe**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert, `ArchitectureTests` compris.

- [ ] **Étape 6 : commit**

```bash
git add Linkpearl/Core/Cache/ICacheConfiguration.cs Linkpearl/Core/Cache/CacheKeeper.cs Linkpearl.Core.Tests/Cache/CacheKeeperTests.cs
git commit -m "feat(cache): gardien du cache, qui attend la présentation et bloque si le dossier disparaît"
```

---

### Tâche 5 : le moteur tient le quota

**Fichiers :**
- Créer : `Linkpearl/Core/Sync/PinnedBlobs.cs`
- Modifier : `Linkpearl/Core/Sync/SyncEngine.cs` (réglages vers la ligne 98, champs vers la ligne 161, `TickAsync` vers la ligne 220)
- Test : `Linkpearl.Core.Tests/Sync/SyncEngineTests.cs`, `Linkpearl.Core.Tests/Sync/SyncPiecesTests.cs`

**Interfaces :**
- Consomme : `IBlobStore.NeedsEviction`, `IBlobStore.EvictionTarget` (tâche 1), `FileSystemBlobStore.SetQuota` (tâche 1).
- Produit : `public static class PinnedBlobs { public static HashSet<BlobHash> Of(IEnumerable<CharacterManifest?> manifests); }`, `SyncEngineSettings.EvictionInterval` (TimeSpan, 30 s).

- [ ] **Étape 1 : écrire les tests qui échouent**

Dans `SyncPiecesTests.cs`, ajouter une classe à la fin du fichier (vérifier les `using` : `Linkpearl.Core.Cache`, `Linkpearl.Core.Manifest`, `Linkpearl.Core.Sync`) :

```csharp
public class PinnedBlobsTests
{
    private static BlobHash H(string content) => BlobHash.OfContent(System.Text.Encoding.UTF8.GetBytes(content));

    private static CharacterManifest With(params string[] contents)
        => new(CharacterManifest.CurrentVersion,
               contents.Select(c => new FileReplacement([$"chara/x/{c}.tex"], H(c), 1)).ToArray(),
               string.Empty, null);

    [Fact]
    public void Tous_les_blobs_des_manifestes_sont_epingles_sans_doublon()
    {
        var pinned = PinnedBlobs.Of([With("a", "b"), null, With("b", "c")]);

        Assert.Equal(3, pinned.Count);
        Assert.Contains(H("a"), pinned);
        Assert.Contains(H("c"), pinned);
    }
}
```

Dans `SyncEngineTests`, ajouter l'aide `PutAsync` (près des autres aides privées comme `Store`) et les trois tests :

```csharp
    private static async Task<BlobHash> PutAsync(FileSystemBlobStore store, byte[] content)
    {
        var hash = BlobHash.OfContent(content);

        await using var writer = await store.BeginWriteAsync(hash, content.Length, default);
        await writer.WriteAsync(content, default);
        Assert.True((await writer.CommitAsync(default)).Accepted);

        return hash;
    }

    [Fact]
    public async Task Au_dela_du_quota_le_cache_evince_ce_qui_n_est_pas_a_l_ecran()
    {
        await using var world = await TwoEnginesAsync();

        IReadOnlyList<VisiblePlayer> sees = [new VisiblePlayer(new GameObjectRef(4, 100), AlicePrint)];

        Assert.True(
            await world.SettleAsync(() => world.BobApplicator.Applied.Count > 0, [], sees),
            "l'apparence n'a jamais été posée : " + world.Describe());

        var stale = await PutAsync(world.BobStore, Encoding.UTF8.GetBytes(new string('x', 200)));
        world.BobStore.SetQuota(world.BlobSize + 10);
        world.Clock.Advance(TimeSpan.FromMinutes(1));

        await world.TickAsync([], sees);

        Assert.False(world.BobStore.TryGetSize(stale, out _));
        Assert.True(world.BobStore.TryGetSize(world.Blob, out _));
    }

    [Fact]
    public async Task Le_cache_garde_toujours_notre_propre_apparence()
    {
        var store = Store("seul");
        var oursContent = Encoding.UTF8.GetBytes("notre tenue, en tout petit");
        var ours = await PutAsync(store, oursContent);
        var stale = await PutAsync(store, Encoding.UTF8.GetBytes(new string('y', 200)));

        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [new FileReplacement(["chara/equipment/e0001/model/c0101e0001_top.mdl"], ours, oursContent.Length)],
            string.Empty, null);

        var identity = CryptoPrimitives.GenerateIdentity();
        _disposables.Add(identity);

        await using var engine = new SyncEngine(
            new PairBook(_clock), new FailingDialer(peerWasAbsent: true), new FixedAppearance(manifest, AlicePrint),
            new RecordingApplicator(), store, PeerId.Of(CryptoPrimitives.ExportPublicPoint(identity)), identity,
            _clock, new SilentLog());

        store.SetQuota(oursContent.Length + 10);
        await engine.TickAsync([], default);

        Assert.True(store.TryGetSize(ours, out _));
        Assert.False(store.TryGetSize(stale, out _));
    }

    [Fact]
    public async Task L_eviction_n_est_verifiee_qu_a_intervalle()
    {
        var store = Store("cadence");
        var identity = CryptoPrimitives.GenerateIdentity();
        _disposables.Add(identity);

        await using var engine = new SyncEngine(
            new PairBook(_clock), new FailingDialer(peerWasAbsent: true), new FixedAppearance(null, AlicePrint),
            new RecordingApplicator(), store, PeerId.Of(CryptoPrimitives.ExportPublicPoint(identity)), identity,
            _clock, new SilentLog());

        await engine.TickAsync([], default);

        var stale = await PutAsync(store, Encoding.UTF8.GetBytes(new string('z', 200)));
        store.SetQuota(10);

        _clock.Advance(TimeSpan.FromSeconds(5));
        await engine.TickAsync([], default);
        Assert.True(store.TryGetSize(stale, out _));

        _clock.Advance(TimeSpan.FromSeconds(30));
        await engine.TickAsync([], default);
        Assert.False(store.TryGetSize(stale, out _));
    }
```

`SyncEngine` est `IAsyncDisposable` : `await using` convient.

- [ ] **Étape 2 : vérifier qu'ils échouent**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj --filter "FullyQualifiedName~SyncEngineTests|FullyQualifiedName~PinnedBlobsTests"`
Attendu : échec de compilation, `PinnedBlobs` n'existe pas.

- [ ] **Étape 3 : l'ensemble épinglé**

Créer `Linkpearl/Core/Sync/PinnedBlobs.cs` :

```csharp
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Sync;

/// <summary>Les blobs qu'une éviction ne doit pas toucher.</summary>
/// <remarks>
/// Ceux des apparences posées à l'écran (Penumbra lit leurs fichiers), de
/// celles en cours de réception (on vient de les payer), et de la nôtre (les
/// pairs nous les demandent).
/// </remarks>
public static class PinnedBlobs
{
    public static HashSet<BlobHash> Of(IEnumerable<CharacterManifest?> manifests)
    {
        var pinned = new HashSet<BlobHash>();

        foreach (var manifest in manifests)
        {
            if (manifest is null)
                continue;

            foreach (var replacement in manifest.Replacements)
                pinned.Add(replacement.Hash);
        }

        return pinned;
    }
}
```

- [ ] **Étape 4 : l'éviction dans le tic**

Dans `SyncEngineSettings`, après `RevocationPatience` :

```csharp
    /// <summary>
    /// Intervalle entre deux vérifications du quota.
    /// </summary>
    /// <remarks>
    /// Vérifier le quota, c'est sommer la taille de chaque blob : des dizaines de
    /// milliers à chaque seconde, pour un dépassement qui se rattrape très bien
    /// trente secondes plus tard.
    /// </remarks>
    public TimeSpan EvictionInterval { get; init; } = TimeSpan.FromSeconds(30);
```

Dans `SyncEngine`, après `private bool _ticking;` :

```csharp
    private DateTimeOffset _lastEvictionCheck = DateTimeOffset.MinValue;
```

Dans `TickAsync`, dernière ligne du bloc `try`, après `ReconcileVisibility(visible);` :

```csharp
            await EvictIfDueAsync(ct).ConfigureAwait(false);
```

Puis ajouter la méthode, juste après `TickAsync` :

```csharp
    /// <summary>
    /// Ramène le cache sous son quota, sans toucher à ce qui sert.
    /// </summary>
    /// <remarks>
    /// Dans le tic, et non ailleurs : c'est le seul fil qui peut lire les
    /// runtimes sans course. Notre apparence est demandée à la source plutôt
    /// qu'au dernier manifeste annoncé, qui n'existe pas tant qu'aucun pair
    /// n'est joint.
    /// </remarks>
    private async Task EvictIfDueAsync(CancellationToken ct)
    {
        if (_clock.UtcNow - _lastEvictionCheck < _settings.EvictionInterval)
            return;

        _lastEvictionCheck = _clock.UtcNow;

        if (_store.NeedsEviction is false)
            return;

        var ours = await _local.CurrentAsync(ct).ConfigureAwait(false);

        var pinned = PinnedBlobs.Of(
            _runtimes.Values
                .SelectMany(runtime => new[] { runtime.AppliedValue, runtime.Exchange?.View.Manifest })
                .Append(ours));

        var before = _store.TotalBytes;
        await _store.EvictToAsync(_store.EvictionTarget, pinned, ct).ConfigureAwait(false);

        _log.Info($"cache au-delà du quota : {(before - _store.TotalBytes) / (1024 * 1024)} Mo libérés.");
    }
```

- [ ] **Étape 5 : vérifier que tout passe**

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert.

- [ ] **Étape 6 : commit**

```bash
git add Linkpearl/Core/Sync Linkpearl.Core.Tests/Sync
git commit -m "feat(sync): tenir le quota du cache, sans évincer ce qui est à l'écran ni notre apparence"
```

---

### Tâche 6 : brancher le gardien dans le plugin

**Fichiers :**
- Modifier : `Linkpearl/Configuration.cs`
- Modifier : `Linkpearl/Plugin.cs`
- Modifier : `Linkpearl/Integration/StatusBarEntry.cs`

**Interfaces :**
- Consomme : `CacheKeeper`, `CacheGateState`, `ICacheConfiguration` (tâche 4).
- Produit : dans `Plugin`, le champ `private readonly CacheKeeper _cacheKeeper;`, les méthodes `StartEngineIfReady()` et `StopEngine()`. `StatusBarEntry.Update(IReadOnlyList<NearbyPlayer> nearby, IEnumerable<PairRecord> pairs, int pending, bool cacheMissing)`. Ne pas toucher à `MainWindow` dans cette tâche : c'est la tâche 7.

Rien ici ne se teste sous Linux : la vérification est le build sans warning et les tests du noyau toujours verts.

- [ ] **Étape 1 : la configuration**

Dans `Configuration.cs` :

1. La classe implémente l'interface : `public sealed class Configuration : IPluginConfiguration, ICacheConfiguration` (ajouter `using Linkpearl.Core.Cache;`).
2. Remplacer la ligne du quota et ajouter les trois drapeaux, à la suite de `CacheDirectory` :

```csharp
    /// <summary>Taille maximale du cache.</summary>
    /// <remarks>
    /// 50 Go : une apparence pèse environ 800 Mo, soit une soixantaine
    /// d'apparences. Une configuration qui avait enregistré l'ancien défaut de
    /// 20 Go le garde.
    /// </remarks>
    public long CacheQuotaBytes { get; set; } = 50L * 1024 * 1024 * 1024;

    /// <summary>La présentation a été fermée une fois.</summary>
    public bool OnboardingSeen { get; set; }

    /// <summary>Le cache a déjà créé son dossier : sa disparition bloque le plugin.</summary>
    public bool CacheEstablished { get; set; }

    /// <summary>L'ancien dossier après un changement, à proposer à la suppression.</summary>
    public string PreviousCacheDirectory { get; set; } = "";
```

- [ ] **Étape 2 : la barre de statut dit le blocage**

Dans `StatusBarEntry.cs` :

1. `private (int Nearby, int Pending)? _shown;` devient `private (int Nearby, int Pending, bool CacheMissing)? _shown;`.
2. Le constructeur appelle `Show(0, 0, false);`.
3. `Update` prend un quatrième paramètre `bool cacheMissing` et appelle `Show(..., pending, cacheMissing)`.
4. `Show(int count, int pending, bool cacheMissing)` compare `_shown == (count, pending, cacheMissing)`, l'affecte, puis, avant le texte habituel :

```csharp
        // Le blocage passe devant tout : tant qu'il dure, rien ne se synchronise,
        // et le nombre de pairs à portée ne voudrait rien dire.
        if (cacheMissing)
        {
            _entry.Text = $"{Glyph} cache introuvable";
            _entry.Tooltip = "Linkpearl : le dossier du cache a disparu, la synchronisation est arrêtée.\n"
                           + "Cliquez pour en choisir un autre.";
            return;
        }
```

- [ ] **Étape 3 : le plugin**

Dans `Plugin.cs` :

1. Ajouter `using Dalamud.Interface.ImGuiNotification;` (et `using Linkpearl.Core.Cache;` s'il n'y est pas).
2. Ajouter le service :

```csharp
    [PluginService] internal static INotificationManager    Notifications   { get; private set; } = null!;
```

3. Remplacer le champ `private readonly FileSystemBlobStore _cache;` par :

```csharp
    /// <summary>
    /// Décide quand le cache existe. L'apparence locale et l'applicateur
    /// reçoivent son magasin commutable, le moteur n'existe que cache ouvert.
    /// </summary>
    private readonly CacheKeeper _cacheKeeper;

    /// <summary>Compte les tics, pour sonder le dossier du cache toutes les cinq secondes.</summary>
    private int _syncTicks;
```

4. Dans le constructeur, remplacer la construction `_cache = new FileSystemBlobStore(...)` (le bloc de plusieurs lignes vers la ligne 174) par :

```csharp
        _cacheKeeper = new CacheKeeper(
            _configuration,
            Path.Combine(_legacyRoot, "cache"),
            clock,
            path => new DriveInfo(Path.GetPathRoot(path) ?? "/").AvailableFreeSpace,
            new PluginLogSink(Log, "cache"));
```

   et remplacer `_cache` par `_cacheKeeper.Store` dans les constructions de `LocalAppearance` et de `RemoteApplicator`.

5. À la toute fin du constructeur, après le bloc `Framework.RunOnFrameworkThread(() => { var left = _applicator.CleanLeftovers(); ... });`, ajouter :

```csharp
        // En dernier : l'ouverture peut lever Lost, qui ouvre la fenêtre, et la
        // fenêtre doit exister.
        _cacheKeeper.Opened += OnCacheOpened;
        _cacheKeeper.Lost += OnCacheLost;
        _cacheKeeper.Start();

        // L'ancien cache se mesure en parcourant tout l'arbre : hors du chargement.
        RunSafely(() => Task.Run(_cacheKeeper.MeasurePrevious));
```

6. Séparer la construction du moteur de `TakeCharacter`. `TakeCharacter` garde `CharacterStorage.Prepare`, `_pairing.Bind(root)`, `_transients.Attach(root)`, puis appelle `StartEngineIfReady();`. Tout le reste (de `_engine = new SyncEngine(` jusqu'au `Log.Information(pairs is 0 ...)` inclus) part dans cette nouvelle méthode, en remplaçant `_cache` par `_cacheKeeper.Store` :

```csharp
    /// <summary>
    /// Construit le moteur si tout ce qu'il lui faut est là : un personnage et
    /// un cache ouvert.
    /// </summary>
    /// <remarks>
    /// Appelé à la connexion et à l'ouverture du cache, dans n'importe quel
    /// ordre : le second appel est celui qui construit. Sur le thread du jeu.
    /// </remarks>
    private void StartEngineIfReady()
    {
        if (_engine is not null || _character is 0 || _pairing.Id is null
            || _cacheKeeper.State is not CacheGateState.Open)
            return;

        _engine = new SyncEngine(
            _pairing.Book,
            new PeerConnector(
                _links,
                new RendezvousEndpoint(_configuration.RendezvousHost, _configuration.RendezvousPort),
                _clock,
                new PluginLogSink(Log, "moteur")),
            _appearance, _applicator, _cacheKeeper.Store, _pairing.Id!.Value, _pairing.Identity!.Key, _clock,
            new PluginLogSink(Log, "moteur"), _engineSettings);
```

   Les lignes existantes qui suivaient (abonnements `PairEnded` et `RevocationDelivered`, calcul de `pairs`, `Log.Information`) se recopient telles quelles à la suite, puis l'accolade fermante de la méthode.

7. Extraire l'arrêt du moteur de `ReleaseCharacter` :

```csharp
    /// <summary>
    /// Arrête le moteur, qui retire des pairs ce qu'il leur a posé.
    /// </summary>
    /// <remarks>
    /// Hors du thread du jeu : le retrait passe par RunOnFrameworkThread, et
    /// l'attendre depuis ce thread-là se bloquerait sur soi-même.
    /// </remarks>
    private void StopEngine()
    {
        var engine = _engine;
        _engine = null;

        if (engine is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Arrêt du moteur en échec.");
            }
        });
    }
```

   `ReleaseCharacter` devient, dans cet ordre : `_pairing.Unbind(); _presence.ForgetRequests(); _transients.Attach(null); StopEngine();`.

8. Les deux réactions au gardien :

```csharp
    /// <summary>Le cache est ouvert : le moteur peut naître, et notre apparence y entrer.</summary>
    /// <remarks>
    /// Peut être levé depuis le pool de threads (un dossier rechoisi) : le
    /// moteur se construit sur le thread du jeu. L'apparence est recapturée
    /// parce que ses fichiers ne sont pas dans un cache neuf.
    /// </remarks>
    private void OnCacheOpened()
    {
        _appearanceChanged.Signal();
        Framework.RunOnFrameworkThread(StartEngineIfReady);
    }

    /// <summary>
    /// Le dossier du cache a disparu : tout s'arrête, et on le dit.
    /// </summary>
    /// <remarks>
    /// Arrêter le moteur rend chaque pair à son apparence par défaut : ses mods
    /// temporaires pointent sur des fichiers qui n'existent plus.
    /// </remarks>
    private void OnCacheLost()
    {
        Framework.RunOnFrameworkThread(() =>
        {
            StopEngine();
            _window.IsOpen = true;

            Notifications.AddNotification(new Notification
            {
                Title = "Linkpearl",
                Content = "Le dossier du cache est introuvable. La synchronisation est arrêtée "
                        + "jusqu'au choix d'un autre dossier.",
                Type = NotificationType.Error,
            });
        });
    }
```

9. Dans `SyncLoopAsync`, remplacer les lignes sur `_appearance` (`Follow`, le commentaire d'anti-rebond, `TryConsume`, `Rebuild`) par :

```csharp
                // Rien ne touche au cache tant qu'il n'est pas ouvert : la capture
                // de notre apparence y écrit. Le signal reste en attente, et la
                // capture part dès l'ouverture.
                if (_cacheKeeper.State is CacheGateState.Open)
                {
                    _appearance.Follow(_state.Self?.Fingerprint);

                    // L'anti-rebond n'est consommé qu'une fois le calme revenu, ou
                    // au plafond : c'est ce qui évite de rehacher pendant qu'on
                    // essaie dix tenues d'affilée.
                    if (_appearanceChanged.TryConsume())
                        _appearance.Rebuild();
                }

                // Le dossier du cache peut être supprimé pendant qu'on joue.
                if (++_syncTicks % 5 == 0)
                    _cacheKeeper.Check();
```

10. Dans `UpdateStatusBar`, passer le quatrième argument : `_cacheKeeper.State is CacheGateState.Missing`.

11. Dans `Dispose`, juste après `_shutdown.Cancel();` : `_cacheKeeper.Opened -= OnCacheOpened; _cacheKeeper.Lost -= OnCacheLost;`.

12. `grep -n "_cache\b" Linkpearl/Plugin.cs` ne doit plus rien trouver.

- [ ] **Étape 4 : vérifier**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `Build succeeded`, `0 Warning(s)`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert.

- [ ] **Étape 5 : commit**

```bash
git add Linkpearl/Configuration.cs Linkpearl/Plugin.cs Linkpearl/Integration/StatusBarEntry.cs
git commit -m "feat(integration): le cache n'existe qu'après la présentation, et son absence arrête tout"
```

---

### Tâche 7 : choisir le cache dans Réglages, et la page de blocage

**Fichiers :**
- Créer : `Linkpearl/Ui/CacheChooser.cs`
- Créer : `Linkpearl/Ui/NameplateLegend.cs`
- Modifier : `Linkpearl/Ui/Icons.cs`
- Modifier : `Linkpearl/Ui/Pages/SettingsPage.cs`
- Modifier : `Linkpearl/Ui/MainWindow.cs`
- Modifier : `Linkpearl/Plugin.cs` (construction de `MainWindow`)

**Interfaces :**
- Consomme : `CacheKeeper` (tâche 4), `_cacheKeeper` dans `Plugin` (tâche 6).
- Produit :
  - `internal sealed class CacheChooser(CacheKeeper keeper)` avec `void Draw()`, `void DrawDialogs()`, `int ShownQuotaGiB`, `static string Format(long bytes)`.
  - `internal static class NameplateLegend` avec `static void Draw()`.
  - `Icons.Folder`, `Icons.Lock`, `Icons.Check`.
  - `MainWindow` : deux paramètres de constructeur ajoutés en fin de liste, `CacheKeeper cacheKeeper, Action showOnboarding` ; méthode `public void OpenNearby()`.

- [ ] **Étape 1 : les icônes**

Dans `Icons.cs`, ajouter aux actions :

```csharp
    public const FontAwesomeIcon Folder  = FontAwesomeIcon.FolderOpen;
    public const FontAwesomeIcon Lock    = FontAwesomeIcon.Lock;
    public const FontAwesomeIcon Check   = FontAwesomeIcon.Check;
```

et ajouter `Folder, Lock, Check,` au tableau `All` : sans cela, l'atlas ne charge pas leur glyphe et elles s'affichent en carré vide.

- [ ] **Étape 2 : la légende des glyphes, partagée**

Créer `Linkpearl/Ui/NameplateLegend.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Linkpearl.Core.Sync;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui;

/// <summary>Ce que veut dire chaque couleur de glyphe, dit au même endroit partout.</summary>
internal static class NameplateLegend
{
    public static void Draw()
    {
        Line(NameplateMark.Online, "pairé et connecté");
        Line(NameplateMark.Available, "utilise Linkpearl, pas encore pairé");
        Line(NameplateMark.Requesting, "vous a envoyé une demande de pairage");
        Line(NameplateMark.Offline, "pairé, hors ligne ou en pause");
        Line(NameplateMark.Trouble, "pairé, mais quelque chose a échoué : voir le carnet");
    }

    private static void Line(NameplateMark mark, string meaning)
    {
        Feedback.StatusDot(NameplateGlyphs.ColorOf(mark));
        ImGui.SameLine();
        Text.Small(meaning);
    }
}
```

Dans `SettingsPage.DrawBadges`, remplacer les cinq appels `Legend(...)` par `NameplateLegend.Draw();` et supprimer la méthode privée `Legend`.

- [ ] **Étape 3 : le sélecteur de cache**

Créer `Linkpearl/Ui/CacheChooser.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Linkpearl.Core.Cache;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui;

/// <summary>
/// Le dossier et le quota du cache, tels que la présentation, les réglages et
/// la page de blocage les montrent.
/// </summary>
/// <remarks>
/// Une instance par fenêtre : chacune dessine ses propres fenêtres de choix de
/// dossier. Le choix part sur le pool de threads, parce qu'il crée un dossier
/// et, après une perte, ouvre un cache entier.
/// </remarks>
internal sealed class CacheChooser(CacheKeeper keeper)
{
    private const float Mo = 1024f * 1024f;

    private readonly FileDialogManager _dialogs = new();

    private volatile string? _error;
    private volatile bool _busy;

    /// <summary>Le dossier dont on a lu l'espace libre, pour ne pas le relire à chaque image.</summary>
    private volatile string? _freeFor;
    private long _free = -1;

    /// <summary>La valeur du curseur pendant qu'on le tire : rien n'est écrit avant qu'on le lâche.</summary>
    private int? _dragging;

    /// <summary>Le quota affiché, curseur en cours de glissement compris.</summary>
    public int ShownQuotaGiB => _dragging ?? keeper.QuotaGiB;

    /// <summary>Les fenêtres de choix de dossier, à dessiner à chaque image.</summary>
    public void DrawDialogs() => _dialogs.Draw();

    public void Draw()
    {
        var shown = keeper.State is CacheGateState.Open && keeper.RestartPending is false
            ? keeper.ActiveRoot ?? keeper.ConfiguredRoot
            : keeper.ConfiguredRoot;

        DrawFolder(shown);
        ImGui.Dummy(Theme.S(0f, Theme.GapM));
        DrawQuota(shown);
    }

    private void DrawFolder(string shown)
    {
        Text.Small("Dossier", Theme.TextMuted);

        var browse = Btn.Measure("Parcourir…", BtnSize.Small, Icons.Folder);
        var display = shown;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - browse - Theme.S(Theme.GapS));
        ImGui.InputText("##cache_folder", ref display, 1024, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        if (Btn.Draw("Parcourir…", BtnTone.Secondary, BtnSize.Small, Icons.Folder, disabled: _busy, id: "cache_browse"))
            _dialogs.OpenFolderDialog("Dossier du cache Linkpearl", OnChosen, StartFolder(shown), isModal: false);

        if (_error is { } error)
            Text.Small(error, Theme.Danger);
        else if (keeper.RestartPending)
            Text.Small("Le nouveau dossier servira au prochain chargement du plugin.", Theme.Idle);
        else
            Text.Small($"Linkpearl n'écrit que dans le sous-dossier {CacheLocation.FolderName}.", Theme.TextFaint);
    }

    private void DrawQuota(string shown)
    {
        Text.Small("Taille maximale", Theme.TextMuted);

        var quota = ShownQuotaGiB;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

        if (ImGui.SliderInt("##cache_quota", ref quota, CacheKeeper.MinQuotaGiB, CacheKeeper.MaxQuotaGiB, "%d Go"))
        {
            var step = CacheKeeper.QuotaStepGiB;
            _dragging = Math.Clamp((int)Math.Round(quota / (double)step) * step,
                                   CacheKeeper.MinQuotaGiB, CacheKeeper.MaxQuotaGiB);
        }

        if (ImGui.IsItemDeactivatedAfterEdit() && _dragging is { } chosen)
            keeper.SetQuota(chosen);

        if (ImGui.IsItemActive() is false)
            _dragging = null;

        var appearances = (int)(ShownQuotaGiB * 1024f / 800f);
        Text.Small($"De quoi garder environ {appearances} apparences de 800 Mo.", Theme.TextFaint);

        if (_freeFor != shown)
        {
            _freeFor = shown;
            Interlocked.Exchange(ref _free, keeper.FreeSpace(shown));
        }

        var free = Interlocked.Read(ref _free);

        if (free < 0)
            return;

        Text.Small($"Espace libre sur ce disque : {Format(free)}.", Theme.TextFaint);

        if (ShownQuotaGiB * CacheKeeper.GiB > free)
            Text.Small("Le quota dépasse l'espace libre : le cache s'arrêtera avant, faute de place.", Theme.Idle);
    }

    private void OnChosen(bool chosen, string path)
    {
        if (chosen is false)
            return;

        _busy = true;

        _ = Task.Run(() =>
        {
            try
            {
                _error = keeper.Choose(path);
            }
            catch (Exception e)
            {
                // Montré, pas avalé : l'utilisateur doit savoir que son choix
                // n'a pas été retenu.
                _error = $"échec : {e.Message}";
            }
            finally
            {
                _freeFor = null;
                _busy = false;
            }
        });
    }

    /// <summary>Le dialogue s'ouvre sur le parent du cache, ou sur les documents.</summary>
    private static string StartFolder(string shown)
    {
        var parent = Path.GetDirectoryName(shown);

        return parent is not null && Directory.Exists(parent)
            ? parent
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    public static string Format(long bytes)
        => bytes >= CacheKeeper.GiB
            ? $"{bytes / (double)CacheKeeper.GiB:0.#} Go"
            : $"{bytes / Mo:0} Mo";
}
```

- [ ] **Étape 4 : la section Cache des réglages**

Dans `SettingsPage` :

1. Le constructeur primaire gagne trois paramètres en fin de liste : `CacheChooser cacheChooser, CacheKeeper cacheKeeper, Action showOnboarding`. Ajouter `using Linkpearl.Core.Cache;`.
2. Dans `Draw()`, insérer `DrawCache();` après `DrawBadges();`, et `DrawOnboarding();` en tout dernier.
3. Ajouter :

```csharp
    private void DrawCache()
    {
        using var card = Card.Begin("settings_cache");

        Text.WithIcon(Icons.Cache, "Cache des apparences", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        Text.Wrapped(
            "Les apparences reçues sont gardées sur le disque pour ne pas les retélécharger. "
          + "Au-delà du quota, les plus anciennes partent, jamais celles à l'écran.");

        ImGui.Dummy(Theme.S(0f, Theme.GapM));
        cacheChooser.Draw();

        if (cacheKeeper.PreviousRoot is not { } previous)
            return;

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        var size = cacheKeeper.PreviousBytes is { } bytes ? CacheChooser.Format(bytes) : "une taille inconnue";
        Text.Small($"L'ancien cache occupe {size} dans {previous}.", Theme.TextMuted);

        if (Btn.Draw("Supprimer l'ancien cache", BtnTone.Danger, BtnSize.Small, Icons.Remove, id: "cache_delete_previous",
                     tooltip: "N'efface que les fichiers de Linkpearl. Tout autre fichier de ce dossier reste."))
        {
            _ = Task.Run(() =>
            {
                try
                {
                    cacheKeeper.DeletePrevious();
                }
                catch (Exception e)
                {
                    Plugin.Log.Warning(e, "Suppression de l'ancien cache en échec.");
                }
            });
        }
    }

    private void DrawOnboarding()
    {
        using var card = Card.Begin("settings_onboarding");

        Text.WithIcon(Icons.Info, "Présentation", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        Text.Wrapped("Le fonctionnement de Linkpearl en six écrans, comme au premier lancement.");
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (Btn.Draw("Revoir la présentation", BtnTone.Secondary, BtnSize.Small, Icons.Info, id: "settings_onboarding"))
            showOnboarding();
    }
```

- [ ] **Étape 5 : `MainWindow`, la page de blocage**

Dans `MainWindow` :

1. Ajouter `using Linkpearl.Core.Cache;` et `using Linkpearl.Ui.Components;` s'il manque, puis les champs `private readonly CacheKeeper _cacheKeeper;` et `private readonly CacheChooser _cacheChooser;`.
2. Ajouter en fin de liste des paramètres du constructeur : `CacheKeeper cacheKeeper, Action showOnboarding`. Dans le corps, avant la construction de `settings` :

```csharp
        _cacheKeeper  = cacheKeeper;
        _cacheChooser = new CacheChooser(cacheKeeper);
```

   et construire `settings` avec `new SettingsPage(configuration, discovery, discover, _backup, setUploadLimited, _cacheChooser, cacheKeeper, showOnboarding)`.
3. Ajouter :

```csharp
    /// <summary>Ouvre la fenêtre sur la page des joueurs autour.</summary>
    public void OpenNearby()
    {
        IsOpen = true;
        _shell.Navigate("nearby");
    }
```

4. Dans `Draw()`, remplacer `_shell.Draw(out var closeRequested, Status());` par :

```csharp
        // Cache introuvable : plus rien d'autre n'a de sens tant qu'un dossier
        // n'est pas rechoisi, et la navigation disparaît.
        var fullScreen = _cacheKeeper.State is CacheGateState.Missing ? DrawCacheMissing : (Action?)null;

        _shell.Draw(out var closeRequested, Status(), fullScreen);
        _cacheChooser.DrawDialogs();
```

5. Ajouter :

```csharp
    private void DrawCacheMissing()
    {
        Text.Title("Dossier du cache introuvable");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        Feedback.Alert(Theme.Danger, Icons.Warning,
            $"Le dossier {_cacheKeeper.LostRoot ?? _cacheKeeper.ConfiguredRoot} n'existe plus. "
          + "La synchronisation est arrêtée, et les pairs affichés sont revenus à leur apparence par défaut.");

        ImGui.Dummy(Theme.S(0f, Theme.GapL));
        Text.Wrapped(
            "Linkpearl ne recrée jamais un dossier disparu : il a peut-être été vidé exprès, ou était sur "
          + "un disque débranché. Choisissez où mettre le cache, et tout repart.");

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        using var card = Card.Begin("cache_missing");
        _cacheChooser.Draw();
    }
```

- [ ] **Étape 6 : `Plugin` passe le gardien**

Dans la construction de `_window` dans `Plugin.cs`, ajouter les deux derniers arguments : `_cacheKeeper, () => { }`. Le second est un bouchon remplacé à la tâche 8 par l'ouverture de la présentation. `_cacheKeeper` est construit avant `_window` si la tâche 6 l'a placé à l'endroit de l'ancien `_cache` : le vérifier.

- [ ] **Étape 7 : vérifier**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `Build succeeded`, `0 Warning(s)`.

- [ ] **Étape 8 : commit**

```bash
git add Linkpearl/Ui Linkpearl/Plugin.cs
git commit -m "feat(ui): dossier et quota du cache dans les réglages, page de blocage si le dossier disparaît"
```

---

### Tâche 8 : la fenêtre de présentation

**Fichiers :**
- Déjà présent : `Linkpearl/Assets/Images/banner.png` (1100×550, fond transparent, commité avec ce plan)
- Modifier : `Linkpearl/Linkpearl.csproj`
- Créer : `Linkpearl/Ui/Onboarding/OnboardingArt.cs`
- Créer : `Linkpearl/Ui/Onboarding/OnboardingWindow.cs`
- Modifier : `Linkpearl/Plugin.cs`

**Interfaces :**
- Consomme : `CacheChooser`, `CacheChooser.ShownQuotaGiB`, `NameplateLegend`, `Icons.Lock`, `Icons.Check`, `MainWindow.OpenNearby` (tâche 7) ; `CacheKeeper.FinishOnboarding` (tâche 4).
- Produit : `internal sealed class OnboardingWindow : ThemedWindow`, constructeur `(ISharedImmediateTexture banner, CacheChooser cacheChooser, Func<bool> penumbraReady, Func<bool> glamourerReady, Action started, Action closed)`, méthode `void Show()`.

- [ ] **Étape 1 : embarquer la bannière**

Dans `Linkpearl.csproj`, ajouter un `ItemGroup` :

```xml
  <!-- La bannière de la présentation. Deux fois la largeur logique de la
       fenêtre, pour rester nette à 200 %. -->
  <ItemGroup>
    <EmbeddedResource Include="Assets\Images\banner.png" LogicalName="Images.banner.png" />
  </ItemGroup>
```

- [ ] **Étape 2 : les illustrations**

Créer `Linkpearl/Ui/Onboarding/OnboardingArt.cs`. Chaque méthode dessine dans la liste de la fenêtre puis réserve sa zone avec `ImGui.Dummy`. Les textes passent par `Glyphs.Safe` comme partout ; les icônes FontAwesome s'écrivent directement (`icon.S()`), leur police étant fusionnée dans celle du corps.

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Linkpearl.Core.Sync;
using System.Numerics;

namespace Linkpearl.Ui.Onboarding;

/// <summary>
/// Les illustrations de la présentation, dessinées et non importées.
/// </summary>
/// <remarks>
/// Dessinées pour rester nettes à toute échelle et prendre les couleurs du
/// thème : une image de l'interface vieillirait au premier changement de
/// teinte.
/// </remarks>
internal static class OnboardingArt
{
    private static uint C(Vector4 color) => ImGui.GetColorU32(color);

    /// <summary>Deux joueurs reliés en direct, et le rendez-vous à l'écart.</summary>
    public static void HowItWorks()
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Theme.S(150f);

        var left = new Vector2(origin.X + width * 0.18f, origin.Y + height * 0.62f);
        var right = new Vector2(origin.X + width * 0.82f, origin.Y + height * 0.62f);
        var server = new Vector2(origin.X + width * 0.5f, origin.Y + height * 0.16f);

        // Le rendez-vous : discret, en pointillé. Il aide à se trouver, rien ne
        // passe par lui.
        Dashed(dl, server, left, Theme.Alpha(Theme.TextFaint, 0.6f));
        Dashed(dl, server, right, Theme.Alpha(Theme.TextFaint, 0.6f));
        dl.AddCircleFilled(server, Theme.S(16f), C(Theme.BgBase));
        dl.AddCircle(server, Theme.S(16f), C(Theme.TextFaint), 24, 1.5f);
        CenteredText(dl, server, Icons.Rendezvous.S(), Theme.TextFaint);
        CenteredText(dl, server + new Vector2(0f, Theme.S(26f)), "rendez-vous", Theme.TextFaint);

        // Le lien direct, chiffré.
        dl.AddLine(left, right, C(Theme.Accent), Theme.S(3f));
        var middle = (left + right) * 0.5f;
        dl.AddCircleFilled(middle, Theme.S(14f), C(Theme.BgSurface));
        dl.AddCircle(middle, Theme.S(14f), C(Theme.Accent), 24, 2f);
        CenteredText(dl, middle, Icons.Lock.S(), Theme.Accent);
        CenteredText(dl, middle + new Vector2(0f, Theme.S(26f)), "chiffré, de joueur à joueur", Theme.Accent);

        Person(dl, left, Theme.Online, "vous");
        Person(dl, right, Theme.Hex(0x6FB6F2), "votre ami");

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>La plaque, le clic droit, la demande chez l'autre.</summary>
    /// <remarks>
    /// Le menu prend la plus grande part : son libellé est celui du vrai menu,
    /// et ne se raccourcit pas pour tenir.
    /// </remarks>
    public static void Pairing()
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Theme.S(118f);
        var gap = Theme.S(10f);
        var side = (width - gap * 2f) * 0.28f;
        var menu = (width - gap * 2f) * 0.44f;

        // 1. La plaque de nom, glyphe orange à droite.
        var plateMin = origin + new Vector2(0f, Theme.S(34f));
        var plateMax = plateMin + new Vector2(side, Theme.S(30f));
        dl.AddRectFilled(plateMin, plateMax, C(Theme.Alpha(Theme.Shadow, 0.55f)), Theme.S(6f));
        dl.AddText(plateMin + Theme.S(10f, 7f), C(Theme.Text), Glyphs.Safe("Alys Fenwood"));
        dl.AddCircleFilled(new Vector2(plateMax.X - Theme.S(12f), (plateMin.Y + plateMax.Y) * 0.5f),
                           Theme.S(5f), C(NameplateGlyphs.ColorOf(NameplateMark.Available)));
        Caption(dl, new Vector2(plateMin.X, plateMax.Y + Theme.S(8f)), "1. un glyphe orange");

        // 2. Le menu clic droit.
        var menuMin = origin + new Vector2(side + gap, 0f);
        var line = Theme.S(22f);
        var menuMax = menuMin + new Vector2(menu, line * 3f + Theme.S(8f));
        dl.AddRectFilled(menuMin, menuMax, C(Theme.BgSurface), Theme.S(6f));
        dl.AddRect(menuMin, menuMax, C(Theme.Border), Theme.S(6f));
        MenuLine(dl, menuMin, 0, line, "Examiner", false, menu);
        MenuLine(dl, menuMin, 1, line, "Envoyer un message", false, menu);
        MenuLine(dl, menuMin, 2, line, "Linkpearl : demander le pairage", true, menu);
        Caption(dl, new Vector2(menuMin.X, menuMax.Y + Theme.S(8f)), "2. clic droit sur le joueur");

        // 3. La demande, chez l'autre.
        var toastMin = origin + new Vector2(side + menu + gap * 2f, Theme.S(18f));
        var toastMax = toastMin + new Vector2(side, Theme.S(56f));
        dl.AddRectFilled(toastMin, toastMax, C(Theme.BgSurface), Theme.S(8f));
        dl.AddRectFilled(toastMin, new Vector2(toastMin.X + Theme.S(3f), toastMax.Y), C(Theme.Accent), Theme.S(8f),
                         ImDrawFlags.RoundCornersLeft);
        dl.AddText(toastMin + Theme.S(10f, 8f), C(Theme.Text), Glyphs.Safe("Demande de pairage"));
        dl.AddText(toastMin + Theme.S(10f, 30f), C(Theme.Online), Icons.Accept.S());
        dl.AddText(toastMin + Theme.S(32f, 30f), C(Theme.Danger), Icons.Decline.S());
        Caption(dl, new Vector2(toastMin.X, toastMax.Y + Theme.S(8f)), "3. l'autre accepte");

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>Une ligne de pair, avec sa pause et ses trois bascules.</summary>
    public static void Controls()
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var rowHeight = Theme.S(44f);

        var max = origin + new Vector2(width, rowHeight);
        dl.AddRectFilled(origin, max, C(Theme.BgSurface), Theme.S(8f));
        dl.AddCircleFilled(origin + new Vector2(Theme.S(18f), rowHeight * 0.5f), Theme.S(5f), C(Theme.Online));
        dl.AddText(origin + new Vector2(Theme.S(32f), rowHeight * 0.5f - ImGui.GetTextLineHeight() * 0.5f),
                   C(Theme.Text), Glyphs.Safe("Alys Fenwood"));

        // De droite à gauche. Les sons sont coupés : on peut bloquer une seule
        // catégorie, et c'est ce que l'image doit montrer.
        var x = max.X - Theme.S(8f);
        x = Toggle(dl, x, origin.Y, rowHeight, Icons.Paused, true);
        x -= Theme.S(12f);
        x = Toggle(dl, x, origin.Y, rowHeight, Icons.Sounds, false);
        x = Toggle(dl, x, origin.Y, rowHeight, Icons.Vfx, true);
        Toggle(dl, x, origin.Y, rowHeight, Icons.Animations, true);

        ImGui.Dummy(new Vector2(width, rowHeight + Theme.S(4f)));
    }

    /// <summary>La jauge du quota, une marque par apparence moyenne de 800 Mo.</summary>
    public static void QuotaGauge(int quotaGiB)
    {
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Theme.S(14f);
        var max = origin + new Vector2(width, height);

        dl.AddRectFilled(origin, max, C(Theme.BgSunken), height * 0.5f);

        // Tant que les marques restent distinctes : au-delà, la jauge
        // deviendrait un aplat.
        var appearances = (int)(quotaGiB * 1024f / 800f);
        var spacing = width / Math.Max(appearances, 1);

        if (spacing >= Theme.S(4f))
        {
            for (var i = 1; i < appearances; i++)
            {
                var px = origin.X + spacing * i;
                dl.AddLine(new Vector2(px, origin.Y + Theme.S(3f)), new Vector2(px, max.Y - Theme.S(3f)),
                           C(Theme.Alpha(Theme.Accent, 0.55f)), 1f);
            }
        }

        dl.AddRect(origin, max, C(Theme.Border), height * 0.5f);
        ImGui.Dummy(new Vector2(width, height + Theme.S(4f)));
    }

    private static float Toggle(ImDrawListPtr dl, float right, float top, float rowHeight,
                                FontAwesomeIcon icon, bool on)
    {
        var size = Theme.S(28f);
        var min = new Vector2(right - size, top + (rowHeight - size) * 0.5f);
        var max = min + new Vector2(size, size);
        var tint = on ? Theme.Accent : Theme.TextFaint;

        dl.AddRectFilled(min, max, C(on ? Theme.AccentMuted : Theme.BgSunken), Theme.S(6f));
        CenteredText(dl, (min + max) * 0.5f, icon.S(), tint);

        if (on is false)
            dl.AddLine(min + Theme.S(6f, 6f), max - Theme.S(6f, 6f), C(Theme.Danger), 2f);

        return min.X - Theme.S(6f);
    }

    private static void Person(ImDrawListPtr dl, Vector2 feet, Vector4 color, string label)
    {
        var head = feet - new Vector2(0f, Theme.S(44f));
        dl.AddCircleFilled(head, Theme.S(11f), C(color));
        dl.AddRectFilled(head + new Vector2(-Theme.S(15f), Theme.S(14f)), feet + new Vector2(Theme.S(15f), 0f),
                         C(color), Theme.S(10f), ImDrawFlags.RoundCornersTop);
        CenteredText(dl, feet + new Vector2(0f, Theme.S(14f)), label, Theme.TextMuted);
    }

    private static void MenuLine(ImDrawListPtr dl, Vector2 menuMin, int index, float line, string text,
                                 bool highlighted, float width)
    {
        var min = menuMin + new Vector2(Theme.S(4f), Theme.S(4f) + line * index);
        var max = min + new Vector2(width - Theme.S(8f), line);

        if (highlighted)
            dl.AddRectFilled(min, max, C(Theme.AccentMuted), Theme.S(4f));

        dl.AddText(min + new Vector2(Theme.S(6f), (line - ImGui.GetTextLineHeight()) * 0.5f),
                   C(highlighted ? Theme.Text : Theme.TextMuted), Glyphs.Safe(text));
    }

    private static void Caption(ImDrawListPtr dl, Vector2 at, string text)
        => dl.AddText(at, C(Theme.TextFaint), Glyphs.Safe(text));

    private static void CenteredText(ImDrawListPtr dl, Vector2 center, string text, Vector4 color)
    {
        var safe = Glyphs.Safe(text);
        var size = ImGui.CalcTextSize(safe);
        dl.AddText(center - size * 0.5f, C(color), safe);
    }

    private static void Dashed(ImDrawListPtr dl, Vector2 from, Vector2 to, Vector4 color)
    {
        var length = Vector2.Distance(from, to);
        var direction = (to - from) / length;
        var dash = Theme.S(5f);

        for (var d = 0f; d < length; d += dash * 2f)
        {
            var end = Math.Min(d + dash, length);
            dl.AddLine(from + direction * d, from + direction * end, C(color), 1.5f);
        }
    }
}
```

Si un libellé déborde de sa zone à 100 %, ajuster les proportions (`0.28` / `0.44`) ou les décalages, jamais le texte du menu.

- [ ] **Étape 3 : la fenêtre**

Créer `Linkpearl/Ui/Onboarding/OnboardingWindow.cs` :

```csharp
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Linkpearl.Ui.Components;
using Linkpearl.Ui.Shell;
using System.Numerics;

namespace Linkpearl.Ui.Onboarding;

/// <summary>
/// La présentation du premier lancement : six écrans, la bannière en tête.
/// </summary>
/// <remarks>
/// Montrée une fois par installation. La fermer par la croix compte comme
/// l'avoir vue : une présentation qui revient à chaque chargement apprend à
/// fermer sans lire. La première fois, sa fermeture ouvre aussi le cache,
/// qui attendait le choix de son dossier.
/// </remarks>
internal sealed class OnboardingWindow : ThemedWindow
{
    /// <summary>Le rapport de la bannière fournie, 1774×887.</summary>
    /// <remarks>
    /// Constant plutôt que lu sur la texture : tant qu'elle se charge, la zone
    /// garde sa taille et la mise en page ne saute pas.
    /// </remarks>
    private const float BannerAspect = 2f;

    private const float WindowWidth  = 580f;
    private const float WindowHeight = 600f;
    private const float StripHeight  = 78f;

    private readonly ISharedImmediateTexture _banner;
    private readonly CacheChooser _cacheChooser;
    private readonly Func<bool> _penumbraReady;
    private readonly Func<bool> _glamourerReady;
    private readonly Action _started;
    private readonly Action _closed;
    private readonly Step[] _steps;

    private int _step;

    private sealed record Step(string Title, string Body, Action? Art);

    public OnboardingWindow(
        ISharedImmediateTexture banner, CacheChooser cacheChooser,
        Func<bool> penumbraReady, Func<bool> glamourerReady, Action started, Action closed)
        : base("Bienvenue dans Linkpearl##onboarding",
               ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking)
    {
        _banner         = banner;
        _cacheChooser   = cacheChooser;
        _penumbraReady  = penumbraReady;
        _glamourerReady = glamourerReady;
        _started        = started;
        _closed         = closed;

        LogicalSizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(WindowWidth, WindowHeight),
            MaximumSize = new Vector2(WindowWidth, WindowHeight),
        };

        _steps =
        [
            new("Bienvenue",
                "Votre apparence moddée, visible par vos amis, directement de joueur à joueur.",
                null),
            new("Comment ça marche",
                "Vos fichiers passent directement de votre jeu à celui de votre ami, chiffrés. "
              + "Le rendez-vous vous aide seulement à vous trouver : il ne stocke rien, et rien "
              + "ne s'échange sans l'accord des deux.",
                OnboardingArt.HowItWorks),
            new("Se pairer, en jeu",
                "Un glyphe à côté du nom signale qui utilise Linkpearl. Clic droit sur le joueur, "
              + "« demander le pairage », et l'autre accepte d'un clic.",
                DrawPairing),
            new("Garder la main",
                "Mettez un pair en pause, ou bloquez les animations, les effets et les sons, pour "
              + "tout le monde ou pour un seul pair. Si une apparence se pose mal, « réappliquer » "
              + "est au clic droit.",
                OnboardingArt.Controls),
            new("Votre cache",
                "Les apparences reçues sont gardées sur le disque pour ne pas les retélécharger. "
              + "Choisissez où, et jusqu'à quelle taille.",
                DrawCache),
            new("Avant de commencer",
                "Linkpearl s'appuie sur Penumbra et Glamourer pour poser les apparences.",
                DrawReady),
        ];
    }

    /// <summary>Ouvre la présentation, toujours sur le premier écran.</summary>
    public void Show()
    {
        _step = 0;
        IsOpen = true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
    }

    /// <summary>Toute fermeture compte : la croix, Échap, ou « C'est parti ».</summary>
    public override void OnClose() => _closed();

    public override void Draw()
    {
        _cacheChooser.DrawDialogs();

        var step = _steps[_step];

        DrawBanner(_step == 0);
        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        Text.Title(step.Title);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        Text.Wrapped(step.Body, Theme.TextMuted);
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        step.Art?.Invoke();

        DrawNavigation();
    }

    private void DrawBanner(bool large)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = large ? width / BannerAspect : Theme.S(StripHeight);
        var size = new Vector2(height * BannerAspect, height);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (width - size.X) * 0.5f);
        ImGui.Image(_banner.GetWrapOrEmpty().Handle, size);
    }

    private void DrawPairing()
    {
        OnboardingArt.Pairing();
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        NameplateLegend.Draw();
    }

    private void DrawCache()
    {
        OnboardingArt.QuotaGauge(_cacheChooser.ShownQuotaGiB);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        _cacheChooser.Draw();
    }

    private void DrawReady()
    {
        var penumbra = _penumbraReady();
        var glamourer = _glamourerReady();

        Prerequisite("Penumbra", penumbra);
        Prerequisite("Glamourer", glamourer);

        if (penumbra is false || glamourer is false)
        {
            ImGui.Dummy(Theme.S(0f, Theme.GapS));
            Feedback.Alert(Theme.Idle, Icons.Warning,
                "Sans eux, Linkpearl peut se pairer mais ne pourra rien afficher. Installez-les ou "
              + "activez-les depuis /xlplugins.");
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapL));
        Feedback.Alert(Theme.Accent, Icons.Backup,
            "Après votre premier pairage, sauvegardez votre identité depuis les réglages : sans cela, "
          + "une réinstallation obligerait à refaire chaque pairage.");
    }

    private static void Prerequisite(string name, bool ready)
        => Text.WithIcon(ready ? Icons.Check : Icons.Decline,
                         ready ? $"{name} est prêt." : $"{name} est absent ou désactivé.",
                         ready ? Theme.Online : Theme.Danger);

    private void DrawNavigation()
    {
        var bottom = ImGui.GetWindowContentRegionMax().Y - Theme.S(32f);
        ImGui.SetCursorPosY(Math.Max(ImGui.GetCursorPosY(), bottom));

        // Les points de progression.
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var radius = Theme.S(4f);
        var spacing = Theme.S(14f);

        for (var i = 0; i < _steps.Length; i++)
        {
            var center = origin + new Vector2(radius + spacing * i, Theme.S(14f));
            dl.AddCircleFilled(center, radius,
                ImGui.GetColorU32(i == _step ? Theme.Accent : Theme.Alpha(Theme.TextFaint, 0.5f)));
        }

        var last = _step == _steps.Length - 1;
        var nextLabel = last ? "C'est parti" : "Suivant";
        var nextWidth = Btn.Measure(nextLabel);
        var backWidth = Btn.Measure("Précédent");
        var right = ImGui.GetWindowContentRegionMax().X;

        if (_step > 0)
        {
            ImGui.SetCursorPosX(right - nextWidth - backWidth - Theme.S(Theme.GapS));

            if (Btn.Draw("Précédent", BtnTone.Ghost, id: "onboarding_back"))
                _step--;

            ImGui.SameLine(0f, Theme.S(Theme.GapS));
        }

        ImGui.SetCursorPosX(right - nextWidth);

        if (Btn.Draw(nextLabel, BtnTone.Primary, id: "onboarding_next"))
        {
            if (last)
            {
                _started();
                IsOpen = false;
            }
            else
            {
                _step++;
            }
        }
    }
}
```

Si la propriété de handle de `IDalamudTextureWrap` ne s'appelle pas `Handle` dans cette version de Dalamud, prendre celle que le compilateur propose (`ImGuiHandle` dans les versions anciennes). Si le contenu d'un écran dépasse la hauteur à 150 %, agrandir `WindowHeight` plutôt que de réduire les illustrations.

- [ ] **Étape 4 : brancher dans le plugin**

Dans `Plugin.cs` :

1. En tête : `using Linkpearl.Ui.Onboarding;` et `using System.Reflection;`. Service et champs :

```csharp
    [PluginService] internal static ITextureProvider        Textures        { get; private set; } = null!;
```

```csharp
    private readonly OnboardingWindow _onboarding;

    /// <summary>L'état des dépendances, relevé par IPC hors du dessin.</summary>
    private volatile bool _penumbraReady;
    private volatile bool _glamourerReady;
    private long _prerequisitesDueAt;
```

2. Dans la construction de `_window`, remplacer le bouchon `() => { }` de la tâche 7 par `() => _onboarding.Show()`.

3. Après `_windows.AddWindow(new RequestToasts(...));` :

```csharp
        _onboarding = new OnboardingWindow(
            Textures.GetFromManifestResource(Assembly.GetExecutingAssembly(), "Images.banner.png"),
            new CacheChooser(_cacheKeeper),
            () => _penumbraReady,
            () => _glamourerReady,
            started: _window.OpenNearby,
            closed: OnOnboardingClosed);
        _windows.AddWindow(_onboarding);
        Framework.Update += UpdatePrerequisites;
```

4. À la fin du constructeur, **juste avant** les lignes `_cacheKeeper.Opened += ...` ajoutées à la tâche 6 :

```csharp
        if (_configuration.OnboardingSeen is false)
            _onboarding.Show();
```

5. Les deux méthodes :

```csharp
    /// <summary>
    /// La présentation se ferme. La première fois, le cache peut enfin naître,
    /// dans le dossier choisi.
    /// </summary>
    /// <remarks>
    /// Sur le pool de threads : ouvrir le cache relit son index, voire tout
    /// son arbre.
    /// </remarks>
    private void OnOnboardingClosed()
    {
        if (_configuration.OnboardingSeen)
            return;

        RunSafely(() => Task.Run(_cacheKeeper.FinishOnboarding));
    }

    /// <summary>
    /// Relève Penumbra et Glamourer toutes les deux secondes, seulement tant
    /// que la présentation est ouverte.
    /// </summary>
    /// <remarks>
    /// Le dessin ne doit rien interroger : un appel IPC à chaque image, pour
    /// une réponse qui ne change qu'au chargement d'un plugin, serait du gâchis.
    /// </remarks>
    private void UpdatePrerequisites(IFramework framework)
    {
        if (_onboarding.IsOpen is false)
            return;

        var now = Environment.TickCount64;

        if (now < _prerequisitesDueAt)
            return;

        _prerequisitesDueAt = now + 2000;
        _penumbraReady = _penumbra.TryGetVersion() is not null;
        _glamourerReady = _glamourer.TryGetVersion() is not null;
    }
```

6. Dans `Dispose`, avec les autres désabonnements de `Framework.Update` : `Framework.Update -= UpdatePrerequisites;`.

- [ ] **Étape 5 : vérifier**

Run : `dotnet build Linkpearl/Linkpearl.csproj -c Release`
Attendu : `Build succeeded`, `0 Warning(s)`.

Run : `dotnet test Linkpearl.Core.Tests/Linkpearl.Core.Tests.csproj`
Attendu : tout vert.

- [ ] **Étape 6 : commit**

```bash
git add Linkpearl/Linkpearl.csproj Linkpearl/Ui Linkpearl/Plugin.cs
git commit -m "feat(ui): présentation illustrée au premier lancement, avec la bannière et le choix du cache"
```

---

### Tâche 9 : essai en jeu et reprise

**Fichiers :**
- Modifier : `docs/reprise.md`

Cette tâche se fait avec l'utilisateur, qui joue : l'agent déploie, donne la liste des vérifications, et consigne ce qui est confirmé.

- [ ] **Étape 1 : déployer**

Run : `./scripts/deploy-plugin-dev.sh`
Attendu : le script se termine sans erreur.

- [ ] **Étape 2 : faire vérifier en jeu**

Pour revoir la présentation comme au premier lancement : mettre `"OnboardingSeen": false` dans `%AppData%\XIVLauncher\pluginConfigs\Linkpearl.json`, puis recharger le plugin. Liste à donner à l'utilisateur :

1. La présentation s'ouvre seule, centrée, bannière nette à 100 % et à 150 % d'échelle Dalamud.
2. Les six écrans défilent. Rien ne déborde, et la légende des couleurs est lisible.
3. Tant qu'elle est ouverte, aucun pair ne reçoit ni n'envoie d'apparence.
4. Choisir un dossier : `LinkpearlCache` y apparaît. Le quota se règle par pas de 5 Go.
5. Penumbra désactivé : croix rouge au sixième écran. Réactivé : coche verte en deux secondes.
6. « C'est parti » ouvre la fenêtre sur « Autour », et les pairs se synchronisent.
7. Au chargement suivant, rien ne réapparaît. « Revoir la présentation » la rouvre.
8. Réglages, quota abaissé sous la taille actuelle : le dossier maigrit en moins d'une minute, sans toucher aux pairs à l'écran.
9. Réglages, autre dossier : le message du prochain chargement s'affiche. Après rechargement, l'ancien est proposé à la suppression, et un fichier étranger qu'on y a déposé survit.
10. Supprimer le dossier du cache en jeu : en cinq secondes au plus, notification, page de blocage, barre de statut « cache introuvable », et les pairs retrouvent leur apparence par défaut. Rechoisir un dossier : tout repart sans rechargement.
11. Plugin éteint, supprimer le dossier, rallumer : page de blocage dès le chargement.

- [ ] **Étape 3 : consigner**

Dans `docs/reprise.md`, ajouter une entrée datée du jour : la présentation et le cache choisi sont faits, spec `superpowers/specs/2026-09-23-onboarding-design.md`, et pour chacun des onze points, confirmé en jeu ou encore à éprouver.

- [ ] **Étape 4 : commit**

```bash
git add docs/reprise.md
git commit -m "docs: présentation et cache choisi, état des essais en jeu"
```
