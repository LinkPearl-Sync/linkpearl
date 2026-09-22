using System.Text;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>Un cache en mémoire : ces tests ne portent que sur la décision.</summary>
internal sealed class FakeBlobStore : IBlobStore
{
    private readonly Dictionary<BlobHash, long> _sizes = [];

    public List<BlobHash> Touched { get; } = [];

    public long TotalBytes => _sizes.Values.Sum();

    public int Count => _sizes.Count;

    public bool IsReadOnly => false;

    public void Add(BlobHash hash, long size) => _sizes[hash] = size;

    public bool TryGetSize(BlobHash hash, out long size) => _sizes.TryGetValue(hash, out size);

    public string PathFor(BlobHash hash)
        => Path.Combine("/cache/blobs", hash.CacheLevel1, hash.CacheLevel2, hash.ToHex());

    public void Touch(BlobHash hash) => Touched.Add(hash);

    public Task<Stream> OpenReadAsync(BlobHash hash, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IBlobWriter> BeginWriteAsync(BlobHash expected, long expectedSize, CancellationToken ct)
        => throw new NotSupportedException();

    public Task EvictToAsync(long targetBytes, IReadOnlySet<BlobHash> pinned, CancellationToken ct)
        => throw new NotSupportedException();
}

/// <summary>
/// Le dernier contrôle avant que la donnée d'un pair n'atteigne Penumbra.
/// </summary>
public class AppearancePlanTests
{
    private const string Top = "chara/equipment/e0001/model/c0101e0001_top.mdl";
    private const string Gloves = "chara/equipment/e0002/model/c0101e0002_glv.mdl";

    private static BlobHash Hash(string content) => BlobHash.OfContent(Encoding.UTF8.GetBytes(content));

    private static CharacterManifest Manifest(params FileReplacement[] replacements)
        => new(CharacterManifest.CurrentVersion, replacements, string.Empty, null);

    [Fact]
    public void Chaque_chemin_de_jeu_recoit_le_fichier_du_cache()
    {
        var store = new FakeBlobStore();
        var hash = Hash("un modèle");
        store.Add(hash, 42);

        var built = AppearancePlanner.TryBuild(
            Manifest(new FileReplacement([Top], hash, 42)), store, Quotas.Default, out var plan, out var why);

        Assert.True(built, why);
        Assert.Equal(store.PathFor(hash), Assert.Contains(Top, plan!.PathMap));
    }

    [Fact]
    public void Le_fichier_pose_est_nomme_par_le_hash_et_par_rien_d_autre()
    {
        // Rien de ce qui vient du réseau ne devient un nom de fichier. Le pair
        // choisit le chemin de jeu remplacé, jamais le fichier qui le remplace.
        var store = new FakeBlobStore();
        var hash = Hash("un modèle");
        store.Add(hash, 42);

        AppearancePlanner.TryBuild(
            Manifest(new FileReplacement([Top], hash, 42)), store, Quotas.Default, out var plan, out _);

        var local = plan!.PathMap[Top];

        Assert.EndsWith(hash.ToHex(), local, StringComparison.Ordinal);
        Assert.DoesNotContain("e0001", local, StringComparison.Ordinal);
    }

    [Fact]
    public void Six_chemins_vers_un_meme_contenu_ne_font_qu_un_fichier()
    {
        // C'est tout l'intérêt de l'adressage par contenu : une texture
        // référencée par six chemins est un blob, pas six.
        var store = new FakeBlobStore();
        var hash = Hash("une texture partagée");
        store.Add(hash, 1024);

        var paths = Enumerable.Range(0, 6)
            .Select(i => $"chara/equipment/e000{i}/material/v0001/mt_c0101e000{i}_top_a.mtrl")
            .ToList();

        AppearancePlanner.TryBuild(
            Manifest(new FileReplacement(paths, hash, 1024)), store, Quotas.Default, out var plan, out var why);

        Assert.NotNull(plan);
        Assert.Equal(6, plan!.PathMap.Count);
        Assert.Single(plan.PathMap.Values.Distinct());
    }

    [Fact]
    public void Un_blob_absent_du_cache_fait_refuser_tout_le_plan()
    {
        // Le cache a pu perdre entre la fin du transfert et l'instant de poser.
        // Une apparence à trous est plus difficile à comprendre qu'un pair resté
        // tel qu'il est.
        var store = new FakeBlobStore();
        var present = Hash("présent");
        store.Add(present, 10);

        var built = AppearancePlanner.TryBuild(
            Manifest(
                new FileReplacement([Top], present, 10),
                new FileReplacement([Gloves], Hash("évincé"), 10)),
            store, Quotas.Default, out var plan, out var why);

        Assert.False(built);
        Assert.Null(plan);
        Assert.Contains("absent du cache", why);
    }

    [Fact]
    public void Deux_contenus_pour_un_meme_chemin_de_jeu_sont_refuses()
    {
        // Lequel poser ? Départager par l'ordre d'itération ne serait pas une
        // décision, seulement un hasard.
        var store = new FakeBlobStore();
        var premier = Hash("premier");
        var second = Hash("second");
        store.Add(premier, 10);
        store.Add(second, 10);

        var built = AppearancePlanner.TryBuild(
            Manifest(
                new FileReplacement([Top], premier, 10),
                new FileReplacement([Top], second, 10)),
            store, Quotas.Default, out _, out var why);

        Assert.False(built);
        Assert.Contains("même chemin de jeu", why);
    }

    [Fact]
    public void Un_manifeste_qui_ne_passerait_plus_la_validation_est_refuse()
    {
        // Revalidation complète, alors que la réception l'a déjà faite : elle
        // protège aussi de ce que nous aurions nous-mêmes abîmé entre les deux.
        var store = new FakeBlobStore();
        var hash = Hash("charge");
        store.Add(hash, 10);

        var built = AppearancePlanner.TryBuild(
            Manifest(new FileReplacement(["chara/equipment/e0001/model/charge.exe"], hash, 10)),
            store, Quotas.Default, out _, out var why);

        Assert.False(built);
        Assert.NotNull(why);
    }

    [Fact]
    public void Ce_qui_est_pose_voit_sa_recence_rafraichie()
    {
        // L'éviction choisit sur la récence : ce qui est à l'écran doit partir
        // en dernier.
        var store = new FakeBlobStore();
        var hash = Hash("un modèle");
        store.Add(hash, 42);

        AppearancePlanner.TryBuild(
            Manifest(new FileReplacement([Top], hash, 42)), store, Quotas.Default, out _, out _);

        Assert.Equal(hash, Assert.Single(store.Touched));
    }

    [Fact]
    public void L_etat_Glamourer_et_les_manipulations_meta_traversent_tels_quels()
    {
        // Deux chaînes opaques, produites par Penumbra et Glamourer. On les
        // transporte sans les comprendre, et on ne peut que borner leur taille.
        var store = new FakeBlobStore();
        var hash = Hash("un modèle");
        store.Add(hash, 42);

        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [new FileReplacement([Top], hash, 42)],
            Convert.ToBase64String("manipulations"u8),
            Convert.ToBase64String("glamourer"u8));

        AppearancePlanner.TryBuild(manifest, store, Quotas.Default, out var plan, out _);

        Assert.Equal(manifest.MetaManipulations, plan!.MetaManipulations);
        Assert.Equal(manifest.GlamourerState, plan.GlamourerState);
    }
}
