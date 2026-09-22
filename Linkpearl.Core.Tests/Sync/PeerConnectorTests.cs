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

public class PeerConnectorTests
{
    private static Announcement Some() => new([new byte[16]], [9]);

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
