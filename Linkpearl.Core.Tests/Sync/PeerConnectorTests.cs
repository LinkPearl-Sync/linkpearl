using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>Un annonceur qui répond selon le lieu, sans réseau.</summary>
internal sealed class ScriptedDialer(Dictionary<string, byte[]?> answers) : IRendezvousDialer
{
    public List<string> Asked { get; } = [];

    public async Task<byte[]?> AnnounceAsync(
        RendezvousAddress at, Announcement announcement, CancellationToken ct)
    {
        lock (Asked)
            Asked.Add(at.Host);

        if (answers.TryGetValue(at.Host, out var answer) is false)
        {
            // Service muet : il ne répond jamais, il ne refuse pas. C'est le cas
            // qui piège une annonce séquentielle.
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        }

        return answer;
    }
}

/// <summary>Un annonceur qui date chaque appel, et dont certains lieux lèvent aussitôt.</summary>
internal sealed class TimedDialer(Dictionary<string, byte[]?> answers, HashSet<string>? failing = null) : IRendezvousDialer
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

    public List<(string Host, TimeSpan At)> Asked { get; } = [];

    public async Task<byte[]?> AnnounceAsync(RendezvousAddress at, Announcement announcement, CancellationToken ct)
    {
        lock (Asked)
            Asked.Add((at.Host, _watch.Elapsed));

        if (failing?.Contains(at.Host) is true)
            throw new IOException("service injoignable");

        if (answers.TryGetValue(at.Host, out var answer) is false)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        }

        return answer;
    }
}

public class PeerConnectorTests
{
    private static Announcement Some() => new([new byte[16]], [9]);

    private static RendezvousAddress At(string host) => new(host, 47900);

    [Fact]
    public async Task Le_cercle_ouvert_apparie_sans_deranger_l_ancrage()
    {
        var dialer = new TimedDialer(new() { ["ouvert.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert.ch")], [At("ancre.ch")], Some(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal("ouvert.ch", match!.Value.At.Host);
        Assert.DoesNotContain(dialer.Asked, asked => asked.Host == "ancre.ch");
    }

    [Fact]
    public async Task L_ancrage_attend_la_tete_laissee_au_cercle_ouvert()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert.ch")], [At("ancre.ch")], Some(), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single(asked => asked.Host == "ancre.ch").At >= TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task Des_services_ouverts_injoignables_liberent_l_ancrage_aussitot()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] }, failing: ["ouvert1.ch", "ouvert2.ch"]);

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("ouvert1.ch"), At("ouvert2.ch")], [At("ancre.ch")], Some(),
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single(asked => asked.Host == "ancre.ch").At < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Sans_cercle_ouvert_l_ancrage_part_aussitot()
    {
        var dialer = new TimedDialer(new() { ["ancre.ch"] = [1] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [], [At("ancre.ch")], Some(), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal("ancre.ch", match!.Value.At.Host);
        Assert.True(dialer.Asked.Single().At < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Nos_propres_candidats_renvoyes_ne_comptent_pas_comme_un_appariement()
    {
        // Un service d'avant le correctif apparie nos deux annonces entre
        // elles quand il est dans les deux cercles sous deux noms : il nous
        // renvoie alors notre propre bloc, qui n'est pas un pair.
        var dialer = new TimedDialer(new() { ["alias.ch"] = [9] });

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("alias.ch")], [], Some(), TimeSpan.Zero, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Null(match);
    }

    [Fact]
    public async Task Un_service_des_deux_cercles_n_est_annonce_qu_une_fois()
    {
        var dialer = new TimedDialer([]);

        var match = await PeerConnector.AnnounceInCirclesAsync(
            dialer, [At("rdv.x.ch")], [new RendezvousAddress("RDV.X.CH", 47900)], Some(),
            TimeSpan.Zero, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        Assert.Null(match);
        Assert.Single(dialer.Asked);
    }

    [Fact]
    public async Task Tous_les_lieux_sont_essayes_en_meme_temps()
    {
        // En séquence, deux personnes en ligne se manqueraient : l'une sur le
        // premier service pendant que l'autre est sur le second.
        var dialer = new ScriptedDialer(new() { ["rapide.ch"] = [1, 2, 3] });

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer,
            [new RendezvousAddress("muet.ch", 47900), new RendezvousAddress("rapide.ch", 47900)],
            Some(),
            TimeSpan.FromSeconds(5),
            default);

        Assert.NotNull(result);
        Assert.Equal("rapide.ch", result!.Value.At.Host);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value.Theirs);
        Assert.Equal(2, dialer.Asked.Count);
    }

    [Fact]
    public async Task Le_lieu_qui_apparie_est_rendu_pour_le_relais()
    {
        // Le relais doit passer par le service que les deux ont atteint.
        var dialer = new ScriptedDialer(new() { ["seul.ch"] = [7] });

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer, [new RendezvousAddress("seul.ch", 443)], Some(), TimeSpan.FromSeconds(5), default);

        Assert.Equal(443, result!.Value.At.Port);
    }

    [Fact]
    public async Task Aucun_lieu_joignable_rend_null_sans_lever()
    {
        var dialer = new ScriptedDialer([]);

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer, [new RendezvousAddress("muet.ch", 47900)], Some(),
            TimeSpan.FromMilliseconds(200), default);

        Assert.Null(result);
    }

    [Fact]
    public async Task Une_liste_vide_rend_null_sans_rien_demander()
    {
        var dialer = new ScriptedDialer([]);

        Assert.Null(await PeerConnector.AnnounceEverywhereAsync(
            dialer, [], Some(), TimeSpan.FromSeconds(1), default));

        Assert.Empty(dialer.Asked);
    }

    [Fact]
    public async Task Un_service_qui_leve_n_empeche_pas_les_autres()
    {
        // Un service injoignable n'est pas une erreur : c'est précisément ce à
        // quoi sert d'en avoir plusieurs.
        var dialer = new ThrowingDialer();

        var result = await PeerConnector.AnnounceEverywhereAsync(
            dialer,
            [new RendezvousAddress("casse.ch", 47900), new RendezvousAddress("bon.ch", 47900)],
            Some(), TimeSpan.FromSeconds(5), default);

        Assert.Equal("bon.ch", result!.Value.At.Host);
    }

    private sealed class ThrowingDialer : IRendezvousDialer
    {
        public Task<byte[]?> AnnounceAsync(
            RendezvousAddress at, Announcement announcement, CancellationToken ct)
            => at.Host is "casse.ch"
                ? throw new IOException("connexion refusée")
                : Task.FromResult<byte[]?>([4, 2]);
    }
}

/// <summary>Le bloc de candidats, que le rendez-vous voit passer sans devoir le lire.</summary>
public class CandidateSealingTests
{
    private static readonly byte[] Secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private static readonly System.Net.IPEndPoint[] Candidates =
    [
        new(System.Net.IPAddress.Parse("203.0.113.7"), 51000),
        new(System.Net.IPAddress.Parse("192.168.1.20"), 51000),
    ];

    [Fact]
    public void Un_bloc_scelle_se_rouvre_avec_le_meme_secret()
    {
        var sealedBlock = PeerConnector.SealCandidates(Secret, Candidates);

        Assert.True(PeerConnector.TryOpenCandidates(Secret, sealedBlock, out var plain));
        Assert.True(CandidateSet.TryDecode(plain, out var decoded, out _));
        Assert.Equal(Candidates, decoded);
    }

    [Fact]
    public void Deux_scellements_n_emploient_jamais_le_meme_nonce()
    {
        // La version 1 scellait sous un nonce nul et une clé fixe par paire :
        // chaque annonce, des deux côtés, réemployait le même couple.
        var first = PeerConnector.SealCandidates(Secret, Candidates);
        var second = PeerConnector.SealCandidates(Secret, Candidates);

        Assert.NotEqual(first[..12], second[..12]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Un_autre_secret_ne_rouvre_pas_le_bloc()
    {
        var other = (byte[])Secret.Clone();
        other[0] ^= 1;

        Assert.False(PeerConnector.TryOpenCandidates(other, PeerConnector.SealCandidates(Secret, Candidates), out _));
    }

    [Fact]
    public void Un_bloc_trop_court_est_refuse_sans_lever()
    {
        Assert.False(PeerConnector.TryOpenCandidates(Secret, new byte[20], out _));
    }
}
