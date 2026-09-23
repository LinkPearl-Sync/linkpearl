using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>
/// La couleur du glyphe à côté du nom, selon ce que l'on sait du joueur.
/// </summary>
public class NameplateMarkTests
{
    private static readonly PlayerFingerprint Her = new(1, 1);
    private static readonly PlayerFingerprint Him = new(2, 2);

    private static readonly PeerView Empty = new(null, null, null, 0, 0, false);

    private static PeerId Peer(byte n)
    {
        var bytes = new byte[16];
        bytes[0] = n;
        return PeerId.FromBytes(bytes);
    }

    private static PairRecord Pair(
        PeerId id, PlayerFingerprint? pinned = null, PairTrust trust = PairTrust.Accepted, bool paused = false)
        => new()
        {
            Id = id, PairSecret = new byte[32], DisplayName = "Amie",
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            Trust = trust, PairedAt = default, PinnedFingerprint = pinned, Paused = paused,
        };

    private static PeerStatus Status(
        PeerId id, PeerSessionState state, PlayerFingerprint? announced = null,
        bool applied = false, bool disputed = false, string? failure = null)
        => new(id, "Amie", state, Empty with { Fingerprint = announced }, applied, disputed, failure, null);

    private static NameplateMark Mark(
        PlayerFingerprint who,
        IReadOnlyList<PairRecord>? pairs = null,
        IReadOnlyList<PeerStatus>? statuses = null,
        IReadOnlyCollection<PlayerFingerprint>? detected = null,
        IReadOnlyCollection<PlayerFingerprint>? requesting = null)
        => NameplateMarks.Build(pairs ?? [], statuses ?? [], detected ?? [], requesting ?? [])
                         .GetValueOrDefault(who, NameplateMark.None);

    [Fact]
    public void Un_joueur_sans_Linkpearl_n_a_rien()
    {
        Assert.Equal(NameplateMark.None, Mark(Her));
    }

    [Fact]
    public void Un_joueur_detecte_et_pas_paire_est_disponible()
    {
        Assert.Equal(NameplateMark.Available, Mark(Her, detected: [Her]));
    }

    [Fact]
    public void Une_demande_en_attente_prime_sur_la_simple_detection()
    {
        // C'est le seul état qui attend une action de notre part.
        Assert.Equal(NameplateMark.Requesting, Mark(Her, detected: [Her], requesting: [Her]));
    }

    [Fact]
    public void Un_pair_connecte_est_en_ligne()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Online,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Connected)], detected: [Her]));
    }

    [Fact]
    public void Un_pair_dont_l_apparence_est_posee_est_en_ligne()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Online,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Applied, applied: true)]));
    }

    [Fact]
    public void L_empreinte_annoncee_relie_un_pair_jamais_epingle()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Online,
            Mark(Her, [Pair(id)], [Status(id, PeerSessionState.Connected, announced: Her)]));
    }

    [Fact]
    public void Un_pair_sans_session_est_hors_ligne()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Offline, Mark(Her, [Pair(id, Her)]));
        Assert.Equal(NameplateMark.Offline,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Disconnected)]));
    }

    [Theory]
    [InlineData(PeerSessionState.Connecting)]
    [InlineData(PeerSessionState.Handshaking)]
    public void Une_connexion_en_cours_n_est_pas_encore_en_ligne(PeerSessionState state)
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Offline, Mark(Her, [Pair(id, Her)], [Status(id, state)]));
    }

    [Fact]
    public void Un_pair_en_pause_est_hors_ligne_meme_s_il_est_detecte()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Offline, Mark(Her, [Pair(id, Her, paused: true)], detected: [Her]));
    }

    [Fact]
    public void Un_echec_signale_se_voit_en_rouge()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Trouble,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Disconnected, failure: "refusé")]));
    }

    [Fact]
    public void Un_echec_d_application_se_voit_meme_connecte()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Trouble,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Connected, failure: "application en échec")]));
    }

    [Fact]
    public void Une_empreinte_contestee_se_voit_en_rouge()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Trouble,
            Mark(Her, [Pair(id, Her)], [Status(id, PeerSessionState.Connected, disputed: true)]));
    }

    [Fact]
    public void Un_pair_bloque_n_a_rien_meme_detecte()
    {
        // Le marquer au-dessus de sa tête le désignerait sans rien apporter.
        var id = Peer(1);

        Assert.Equal(NameplateMark.None,
            Mark(Her, [Pair(id, Her, PairTrust.Blocked)], detected: [Her], requesting: [Her]));
    }

    [Fact]
    public void Un_pairage_pas_encore_conclu_reste_disponible()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Available, Mark(Her, [Pair(id, Her, PairTrust.Pending)], detected: [Her]));
    }

    [Fact]
    public void Un_pair_retire_redevient_un_inconnu()
    {
        var id = Peer(1);

        Assert.Equal(NameplateMark.Available, Mark(Her, [Pair(id, Her, PairTrust.Revoked)], detected: [Her]));
    }

    [Fact]
    public void Chaque_joueur_a_sa_propre_marque()
    {
        var id = Peer(1);
        var marks = NameplateMarks.Build(
            [Pair(id, Her)], [Status(id, PeerSessionState.Applied, applied: true)], [Her, Him], []);

        Assert.Equal(NameplateMark.Online, marks[Her]);
        Assert.Equal(NameplateMark.Available, marks[Him]);
    }
}
