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
    public void Choisir_un_dossier_apres_la_perte_efface_le_changement_en_attente_et_n_offre_pas_le_disparu()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        keeper.Start();

        // Un changement était en attente avant la perte.
        Assert.Null(keeper.Choose(Chosen("ailleurs")));
        Assert.True(keeper.RestartPending);

        Directory.Delete(DefaultRoot, recursive: true);
        keeper.Check();
        Assert.Equal(CacheGateState.Missing, keeper.State);

        Assert.Null(keeper.Choose(Chosen("nouveau")));

        Assert.False(keeper.RestartPending);
        Assert.Null(keeper.PreviousRoot);
        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.Equal(Path.Combine(_root, "nouveau", "LinkpearlCache"), keeper.ActiveRoot);
    }

    [Fact]
    public void Un_rechoix_rate_en_etat_manquant_ne_releve_pas_lost_une_seconde_fois()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        Directory.Delete(DefaultRoot, recursive: true);
        keeper.Check();
        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(1, lost);

        // Un dossier où "blobs" est déjà un fichier : la construction du
        // magasin échoue, et le rechoix rate.
        var brokenParent = Path.Combine(_root, "casse");
        var brokenCache = Path.Combine(brokenParent, "LinkpearlCache");
        Directory.CreateDirectory(brokenCache);
        File.WriteAllBytes(Path.Combine(brokenCache, "blobs"), [1]);

        var error = keeper.Choose(brokenParent);

        Assert.NotNull(error);
        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(1, lost);
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
    public void Choisir_un_autre_dossier_pendant_la_presentation_garde_l_ancien_a_la_suppression()
    {
        // Un utilisateur existant (avant l'onboarding) a déjà un cache au
        // dossier par défaut. En choisir un autre pendant la présentation ne
        // doit pas l'abandonner silencieusement : il doit rester proposable
        // à la suppression une fois le nouveau ouvert.
        Directory.CreateDirectory(DefaultRoot);
        File.WriteAllBytes(Path.Combine(DefaultRoot, "temoin.txt"), [1]);

        var keeper = Keeper();
        keeper.Start();

        Assert.Null(keeper.Choose(Chosen("ailleurs")));
        keeper.FinishOnboarding();

        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.Equal(Path.Combine(_root, "ailleurs", "LinkpearlCache"), keeper.ActiveRoot);
        Assert.Equal(DefaultRoot, keeper.PreviousRoot);
    }

    [Fact]
    public void Un_cache_etabli_dont_la_racine_existe_mais_est_vide_bloque_au_chargement()
    {
        _configuration.OnboardingSeen = true;
        _configuration.CacheEstablished = true;
        _configuration.CacheDirectory = Path.Combine(_root, "vide", "LinkpearlCache");
        Directory.CreateDirectory(_configuration.CacheDirectory); // racine seule, sans blobs/ ni incoming/

        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void Un_dossier_vide_sans_etre_supprime_est_vu_au_sondage()
    {
        _configuration.OnboardingSeen = true;
        var keeper = Keeper();
        var lost = 0;
        keeper.Lost += () => lost++;
        keeper.Start();

        Directory.Delete(Path.Combine(DefaultRoot, "blobs"), recursive: true);
        Directory.Delete(Path.Combine(DefaultRoot, "incoming"), recursive: true);
        keeper.Check();

        Assert.Equal(CacheGateState.Missing, keeper.State);
        Assert.Equal(1, lost);
    }

    [Fact]
    public void Rechoisir_le_meme_dossier_vide_en_etat_manquant_le_rouvre()
    {
        _configuration.OnboardingSeen = true;
        _configuration.CacheEstablished = true;
        var cacheRoot = Path.Combine(_root, "mien", "LinkpearlCache");
        _configuration.CacheDirectory = cacheRoot;
        Directory.CreateDirectory(cacheRoot); // racine seule, vidée : pas de blobs/ ni incoming/

        var keeper = Keeper();
        var opened = 0;
        keeper.Opened += () => opened++;
        keeper.Start();

        Assert.Equal(CacheGateState.Missing, keeper.State);

        // Un choix explicite garde le droit de recréer les sous-dossiers,
        // même sur le même chemin que celui qui vient de bloquer.
        Assert.Null(keeper.Choose(Path.Combine(_root, "mien")));

        Assert.Equal(CacheGateState.Open, keeper.State);
        Assert.Equal(cacheRoot, keeper.ActiveRoot);
        Assert.True(Directory.Exists(Path.Combine(cacheRoot, "blobs")));
        Assert.True(Directory.Exists(Path.Combine(cacheRoot, "incoming")));
        Assert.Equal(1, opened);
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
