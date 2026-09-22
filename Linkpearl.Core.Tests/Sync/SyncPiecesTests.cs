using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

internal sealed class MovableClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

public class DebouncerTests
{
    private static Debouncer New(MovableClock clock)
        => new(clock, TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(5));

    [Fact]
    public void Rien_ne_se_declenche_sans_signal()
    {
        Assert.False(New(new MovableClock()).TryConsume());
    }

    [Fact]
    public void Une_rafale_ne_declenche_qu_un_seul_recalcul()
    {
        // Un changement de tenue produit une dizaine d'événements en quelques
        // centaines de millisecondes.
        var clock = new MovableClock();
        var debouncer = New(clock);

        for (var i = 0; i < 10; i++)
        {
            debouncer.Signal();
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.False(debouncer.TryConsume());
        }

        clock.Advance(TimeSpan.FromMilliseconds(800));
        Assert.True(debouncer.TryConsume());
        Assert.False(debouncer.TryConsume());
    }

    [Fact]
    public void Des_changements_continus_finissent_par_se_declencher_au_plafond()
    {
        // Sans plafond, quelqu'un qui bricole son apparence pendant dix minutes
        // ne serait jamais synchronisé.
        var clock = new MovableClock();
        var debouncer = New(clock);

        for (var i = 0; i < 100; i++)
        {
            debouncer.Signal();
            clock.Advance(TimeSpan.FromMilliseconds(100));

            if (debouncer.TryConsume())
                return;
        }

        Assert.Fail("le plafond n'a jamais été atteint");
    }

    [Fact]
    public void L_annulation_efface_l_attente()
    {
        var clock = new MovableClock();
        var debouncer = New(clock);

        debouncer.Signal();
        debouncer.Cancel();
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False(debouncer.TryConsume());
    }
}

public class VisibilityMatcherTests
{
    private static PeerId Peer(string seed)
        => PeerId.Of(System.Text.Encoding.UTF8.GetBytes(seed));

    private static PlayerFingerprint Print(string name)
        => PlayerFingerprint.Of(name, 21);

    [Fact]
    public void Un_pair_annonce_et_visible_est_reconnu()
    {
        var visible = new List<VisiblePlayer> { new(new GameObjectRef(4, 100), Print("jhalen")) };
        var announced = new Dictionary<PeerId, PlayerFingerprint> { [Peer("a")] = Print("jhalen") };

        var match = Assert.Single(VisibilityMatcher.Match(visible, announced));

        Assert.Equal(Peer("a"), match.Peer);
        Assert.Equal(4, match.Object.ObjectIndex);
    }

    [Fact]
    public void Un_joueur_visible_sans_pair_correspondant_est_ignore()
    {
        var visible = new List<VisiblePlayer> { new(new GameObjectRef(4, 100), Print("inconnu")) };
        var announced = new Dictionary<PeerId, PlayerFingerprint> { [Peer("a")] = Print("jhalen") };

        Assert.Empty(VisibilityMatcher.Match(visible, announced));
    }

    [Fact]
    public void Un_pair_annonce_mais_absent_du_champ_n_est_pas_reconnu()
    {
        var announced = new Dictionary<PeerId, PlayerFingerprint> { [Peer("a")] = Print("jhalen") };

        Assert.Empty(VisibilityMatcher.Match([], announced));
    }

    [Fact]
    public void Deux_pairs_revendiquant_la_meme_empreinte_ne_donnent_aucun_appariement()
    {
        // L'un des deux ment. Appliquer au hasard ferait porter à quelqu'un
        // l'apparence choisie par un tiers, visible chez nous seuls.
        var visible = new List<VisiblePlayer> { new(new GameObjectRef(4, 100), Print("jhalen")) };
        var announced = new Dictionary<PeerId, PlayerFingerprint>
        {
            [Peer("a")] = Print("jhalen"),
            [Peer("b")] = Print("jhalen"),
        };

        Assert.Empty(VisibilityMatcher.Match(visible, announced));
    }

    [Fact]
    public void L_empreinte_depend_du_monde_autant_que_du_nom()
    {
        // Deux homonymes sur deux mondes sont deux personnes différentes.
        Assert.NotEqual(PlayerFingerprint.Of("jhalen", 21), PlayerFingerprint.Of("jhalen", 36));
    }

    [Fact]
    public void L_empreinte_fait_l_aller_retour_binaire()
    {
        var original = Print("jhalen");
        Assert.Equal(original, PlayerFingerprint.FromBytes(original.ToBytes()));
    }
}

public class PairBookTests
{
    private static PairBook New() => new(new MovableClock());

    private static (PairingCode Code, PeerId Ours, byte[] TheirKey) Invitation()
    {
        using var theirs = CryptoPrimitives.GenerateIdentity();
        using var ours = CryptoPrimitives.GenerateIdentity();

        var theirKey = CryptoPrimitives.ExportPublicPoint(theirs);

        return (PairingCode.Create(PeerId.Of(theirKey), "rdv.exemple.ch"),
                PeerId.Of(CryptoPrimitives.ExportPublicPoint(ours)),
                theirKey);
    }

    [Fact]
    public void Un_pair_ajoute_n_est_pas_encore_verifie_de_vive_voix()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();

        var record = book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");

        Assert.Equal(PairTrust.Accepted, record.Trust);
        Assert.False(record.KeyVerified);
    }

    [Fact]
    public void Un_pair_ajoute_passe_le_handshake_et_devient_actif()
    {
        // Sans cela, la première connexion échouerait et l'utilisateur ne verrait
        // jamais la demande à confirmer. Rien ne lui est appliqué pour autant.
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");

        Assert.True(book.IsAuthorized(theirKey));
        Assert.Single(book.Active);
    }

    [Fact]
    public void Un_pair_accepte_devient_actif()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");

        Assert.Single(book.Active);
    }

    [Fact]
    public void Un_pair_bloque_n_est_plus_autorise()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Gêneur", "rdv.exemple.ch");
        book.Block(code.Id);

        Assert.False(book.IsAuthorized(theirKey));
        Assert.Empty(book.Active);
    }

    [Fact]
    public void Un_pair_en_pause_reste_autorise_mais_sort_des_actifs()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");
        book.SetPaused(code.Id, true);

        Assert.True(book.IsAuthorized(theirKey));
        Assert.Empty(book.Active);
    }

    [Fact]
    public void Un_inconnu_n_est_jamais_autorise()
    {
        using var stranger = CryptoPrimitives.GenerateIdentity();
        Assert.False(New().IsAuthorized(CryptoPrimitives.ExportPublicPoint(stranger)));
    }

    [Fact]
    public void Les_deux_pairs_derivent_le_meme_secret_depuis_le_meme_code()
    {
        // Celui qui invite et celui qui colle doivent tomber sur le même secret,
        // sans quoi leurs jetons de rendez-vous ne coïncideraient jamais.
        using var alice = CryptoPrimitives.GenerateIdentity();
        using var bob = CryptoPrimitives.GenerateIdentity();
        var aliceId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(alice));
        var bobId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(bob));

        var codeFromAlice = PairingCode.Create(aliceId, "rdv.exemple.ch");

        var bobBook = new PairBook(new MovableClock());
        var chezBob = bobBook.Add(
            aliceId, CryptoPrimitives.ExportPublicPoint(alice), codeFromAlice.PairingNonce,
            bobId, "Alice", "rdv.exemple.ch");

        var chezAlice = PairSecret.Derive(codeFromAlice.PairingNonce, aliceId, bobId);

        Assert.Equal(chezAlice, chezBob.PairSecret);
    }

    [Fact]
    public void La_cle_complete_est_apprise_au_premier_handshake()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();

        // Un pair peut exister sans clé connue, par exemple après une migration.
        book.Load([new PairRecord
        {
            Id = code.Id, PairSecret = new byte[32], DisplayName = "Amie",
            RendezvousHost = "rdv.exemple.ch", Trust = PairTrust.Accepted, PairedAt = default,
        }]);

        Assert.Null(book.Find(code.Id)!.PublicKey);
        Assert.True(book.IsAuthorized(theirKey));
        Assert.Equal(theirKey, book.Find(code.Id)!.PublicKey);
    }

    [Fact]
    public void Une_cle_qui_changerait_apres_avoir_ete_apprise_est_refusee()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");

        // Deux clés différentes de même empreinte seraient une contradiction.
        var falsifiee = theirKey.ToArray();
        falsifiee[10] ^= 0x01;

        Assert.False(book.IsAuthorized(falsifiee));
    }

    [Fact]
    public void L_empreinte_du_personnage_s_epingle()
    {
        var book = New();
        var (code, ours, theirKey) = Invitation();
        book.Add(code.Id, theirKey, code.PairingNonce, ours, "Amie", "rdv.exemple.ch");

        book.PinFingerprint(code.Id, PlayerFingerprint.Of("amie", 21));

        Assert.Equal(PlayerFingerprint.Of("amie", 21), book.Find(code.Id)!.PinnedFingerprint);
    }
}
