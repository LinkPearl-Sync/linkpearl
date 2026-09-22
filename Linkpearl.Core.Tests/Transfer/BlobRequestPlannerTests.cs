using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Transfer;
using Xunit;

namespace Linkpearl.Core.Tests.Transfer;

public class BlobRequestPlannerTests
{
    /// <summary>Cache en mémoire : le planificateur n'a besoin que des tailles.</summary>
    private sealed class FakeStore(params (BlobHash Hash, long Size)[] present) : IBlobStore
    {
        private readonly Dictionary<BlobHash, long> _present = present.ToDictionary(p => p.Hash, p => p.Size);

        public bool TryGetSize(BlobHash hash, out long size) => _present.TryGetValue(hash, out size);

        public long TotalBytes => _present.Values.Sum();
        public int Count => _present.Count;
        public bool IsReadOnly => false;
        public string PathFor(BlobHash hash) => hash.ToHex();
        public void Touch(BlobHash hash) { }
        public Task<Stream> OpenReadAsync(BlobHash hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<IBlobWriter> BeginWriteAsync(BlobHash e, long s, CancellationToken ct) => throw new NotSupportedException();
        public Task EvictToAsync(long target, IReadOnlySet<BlobHash> pinned, CancellationToken ct) => Task.CompletedTask;
    }

    private static BlobHash H(string content) => BlobHash.OfContent(System.Text.Encoding.UTF8.GetBytes(content));

    private static CharacterManifest Manifest(params (string Content, long Size)[] entries)
        => new(CharacterManifest.CurrentVersion,
               entries.Select(e => new FileReplacement([$"chara/x/{e.Content}.tex"], H(e.Content), e.Size)).ToArray(),
               "bWV0YQ==", null);

    [Fact]
    public void Rien_a_demander_quand_tout_est_deja_en_cache()
    {
        var manifest = Manifest(("a", 10), ("b", 20));
        var store = new FakeStore((H("a"), 10), (H("b"), 20));

        var plan = BlobRequestPlanner.Plan(manifest, store);

        Assert.Empty(plan.Missing);
        Assert.Equal(0, plan.MissingBytes);
        Assert.Equal(30, plan.CachedBytes);
    }

    [Fact]
    public void Seuls_les_blobs_absents_sont_demandes()
    {
        var manifest = Manifest(("a", 10), ("b", 20), ("c", 30));
        var store = new FakeStore((H("b"), 20));

        var plan = BlobRequestPlanner.Plan(manifest, store);

        Assert.Equal(2, plan.Missing.Count);
        Assert.Equal(40, plan.MissingBytes);
        Assert.Equal(20, plan.CachedBytes);
    }

    [Fact]
    public void Les_manquants_sont_demandes_du_plus_petit_au_plus_grand()
    {
        // Ce tri n'est pas neutre : il donne un retour visuel immédiat et permet
        // d'appliquer une partie de l'apparence plus tôt.
        var manifest = Manifest(("gros", 5_000_000), ("petit", 1_000), ("moyen", 100_000));
        var plan = BlobRequestPlanner.Plan(manifest, new FakeStore());

        Assert.Equal([1_000L, 100_000L, 5_000_000L], plan.Missing.Select(m => m.Size).ToArray());
    }

    [Fact]
    public void Un_manifeste_vide_donne_un_plan_vide()
    {
        var plan = BlobRequestPlanner.Plan(Manifest(), new FakeStore());

        Assert.Empty(plan.Missing);
        Assert.Equal(0, plan.MissingBytes);
    }

    [Fact]
    public void Un_meme_blob_vise_par_plusieurs_entrees_n_est_demande_qu_une_fois()
    {
        // Le manifeste regroupe déjà par hash, mais un pair malveillant peut
        // répéter une entrée pour nous faire télécharger deux fois.
        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [
                new FileReplacement(["chara/x/a.tex"], H("a"), 10),
                new FileReplacement(["chara/x/b.tex"], H("a"), 10),
            ],
            "bWV0YQ==", null);

        var plan = BlobRequestPlanner.Plan(manifest, new FakeStore());

        Assert.Single(plan.Missing);
        Assert.Equal(10, plan.MissingBytes);
    }
}
