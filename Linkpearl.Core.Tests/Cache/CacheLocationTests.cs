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
