using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

internal sealed class RecordingGate(bool admit) : IGroupGate
{
    public List<byte[]> Seen { get; } = [];

    public bool Admits(PairRecord pair, byte[] publicKey)
    {
        lock (Seen)
            Seen.Add(publicKey);

        return admit;
    }
}

public sealed class PeerSessionGroupTests : IDisposable
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _keys = [];

    public void Dispose()
    {
        foreach (var key in _keys)
            key.Dispose();
    }

    private (System.Security.Cryptography.ECDsa Key, byte[] Public, PeerId Id) Identity()
    {
        var key = CryptoPrimitives.GenerateIdentity();
        _keys.Add(key);
        var point = CryptoPrimitives.ExportPublicPoint(key);
        return (key, point, PeerId.Of(point));
    }

    /// <summary>Ce que le planificateur donnerait à chacun : même secret, même identifiant, origines miroir.</summary>
    private PairRecord Seeing(PlayerFingerprint ours, PlayerFingerprint theirs)
    {
        var secret = GroupDerivation.MemberPairSecret(Secret, ours, theirs);

        return new PairRecord
        {
            Id = GroupDerivation.RuntimeId(secret),
            PairSecret = secret,
            DisplayName = "Membre",
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            Trust = PairTrust.Accepted,
            PairedAt = _clock.UtcNow,
            PinnedFingerprint = theirs,
            Group = new GroupOrigin(GroupId.Of(Secret), ours, theirs),
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deux_membres_etablissent_une_session_quel_que_soit_l_ordre(bool aliceFirst)
    {
        // Les identifiants de runtime sont les mêmes des deux côtés : les
        // comparer, comme pour une paire, ferait deux initiateurs ou deux
        // répondeurs. Ce sont les empreintes qui décident.
        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();
        var aliceGate = new RecordingGate(admit: true);
        var bobGate = new RecordingGate(admit: true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Task<PeerSession?> Alice() => PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token, aliceGate);

        Task<PeerSession?> Bob() => PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token, bobGate);

        var (first, second) = aliceFirst ? (Alice(), Bob()) : (Bob(), Alice());
        var sessions = await Task.WhenAll(first, second);

        Assert.All(sessions, Assert.NotNull);
        Assert.Equal(bob.Public, Assert.Single(aliceGate.Seen));
        Assert.Equal(alice.Public, Assert.Single(bobGate.Seen));

        foreach (var session in sessions)
            await session!.DisposeAsync();
    }

    [Fact]
    public async Task Une_porte_qui_refuse_empeche_la_session()
    {
        // L'empreinte de bob est la plus petite : bob initie, alice répond et
        // vérifie au dernier message. C'est son refus qu'on observe, sans
        // attendre le délai de garde de quinze secondes de l'autre côté.
        Assert.True(string.CompareOrdinal(BobPrint.ToString(), AlicePrint.ToString()) < 0);

        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var aliceSide = PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: false));

        var bobSide = PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: true));

        Assert.Null(await aliceSide);

        if (await bobSide is { } opened)
            await opened.DisposeAsync();
    }

    [Fact]
    public async Task Un_pair_de_groupe_sans_porte_est_refuse()
    {
        var alice = Identity();
        var bob = Identity();
        var (aliceLink, bobLink) = HeldLink.Pair();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var aliceSide = PeerSession.EstablishAsync(
            aliceLink, Seeing(AlicePrint, BobPrint), alice.Id, alice.Key, _clock, new SilentLog(), timeout.Token);

        var bobSide = PeerSession.EstablishAsync(
            bobLink, Seeing(BobPrint, AlicePrint), bob.Id, bob.Key, _clock, new SilentLog(), timeout.Token,
            new RecordingGate(admit: true));

        Assert.Null(await aliceSide);

        if (await bobSide is { } opened)
            await opened.DisposeAsync();
    }
}
