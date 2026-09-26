using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Harness;

public sealed record OpenCircleSettings(
    IReadOnlyList<RendezvousAddress> Open, RendezvousAddress Anchor, RendezvousAddress Authority,
    byte[] AuthorityKey, int TimeoutSeconds);

/// <summary>Journal de console qui compte les appariements passés par le cercle ouvert.</summary>
internal sealed class CountingLog(string who) : ILogSink
{
    private readonly ConsoleLog _inner = new(who);
    private int _open;

    public int OpenMatches => Volatile.Read(ref _open);

    public void Debug(string message) => _inner.Debug(message);

    public void Info(string message)
    {
        if (message.Contains("(cercle ouvert)", StringComparison.Ordinal))
            Interlocked.Increment(ref _open);

        _inner.Info(message);
    }

    public void Warning(string message, Exception? exception = null) => _inner.Warning(message, exception);
}

/// <summary>
/// La preuve que le cercle ouvert marche, et que son repli aussi.
/// </summary>
/// <remarks>
/// Les services tournent à côté, lancés à la main, comme pour la fédération.
/// Une autorité réelle ne liste personne avant soixante-douze heures : sa
/// liste n'est donc vérifiée qu'en signature. Les deux scénarios suivants
/// emploient une liste signée par une clé du harnais, qu'un <see cref="OpenCircle"/>
/// accepte comme il accepterait celle de l'autorité.
///
/// Le second scénario ne liste que des services morts : l'apparence doit
/// passer quand même, par l'ancrage. Un cercle ouvert qui deviendrait une
/// dépendance ferait échouer ce cas.
/// </remarks>
public static class OpenCircleRun
{
    public static async Task<bool> ExecuteAsync(OpenCircleSettings settings, CancellationToken ct)
    {
        Console.WriteLine($"Cercle ouvert : {string.Join(", ", settings.Open)}");
        Console.WriteLine($"Ancrage : {settings.Anchor}");
        Console.WriteLine($"Autorité : {settings.Authority}");
        Console.WriteLine();

        var ok = await CheckAuthorityAsync(settings, ct).ConfigureAwait(false);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        ok &= await ScenarioAsync(
            "cercle ouvert", Sign(key, settings.Open), key, settings, expectOpen: true, ct).ConfigureAwait(false);

        ok &= await ScenarioAsync(
            "repli sur l'ancrage",
            Sign(key, [new RendezvousAddress("127.0.0.1", 47998), new RendezvousAddress("127.0.0.1", 47997)]),
            key, settings, expectOpen: false, ct).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(ok ? "TOUT EST PASSÉ" : "ÉCHEC : voir ci-dessus.");
        return ok;
    }

    private static async Task<bool> CheckAuthorityAsync(OpenCircleSettings settings, CancellationToken ct)
    {
        Console.WriteLine("── liste de l'autorité ──");

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));

            await using var client = new RendezvousClient();
            await client.ConnectAsync(settings.Authority.Host, settings.Authority.Port, deadline.Token).ConfigureAwait(false);

            var (document, failure) = await client.QueryConsensusAsync(deadline.Token).ConfigureAwait(false);

            if (document is null)
            {
                Console.WriteLine($"   ÉCHEC : {failure}");
                return false;
            }

            if (ServiceConsensus.TryVerify(
                    document, [settings.AuthorityKey], DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var list, out var why) is false)
            {
                Console.WriteLine($"   ÉCHEC : {why}");
                return false;
            }

            Console.WriteLine($"   liste de l'autorité vérifiée : version {list!.Version}, {list.Entries.Count} service(s)");
            return true;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            Console.WriteLine($"   ÉCHEC : autorité injoignable ({e.Message})");
            return false;
        }
    }

    private static byte[] Sign(ECDsa key, IReadOnlyList<RendezvousAddress> services)
    {
        var issued = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entries = services
            .Select((at, i) => new ConsensusEntry(ServiceConsensus.Canonical(at), $"ouvert {i + 1}", [.. Enumerable.Repeat((byte)(i + 1), 8)]))
            .ToList();

        return ServiceConsensus.Sign(
            new ServiceConsensus(1, issued, issued + (long)ServiceConsensus.Lifetime.TotalSeconds, entries), key);
    }

    private static async Task<bool> ScenarioAsync(
        string name, byte[] document, ECDsa key, OpenCircleSettings settings, bool expectOpen, CancellationToken ct)
    {
        Console.WriteLine($"── {name} ──");

        var clock = new RealClock();
        var root = Path.Combine(Path.GetTempPath(), "linkpearl-cercle-" + Guid.NewGuid().ToString("N"));

        using var aliceIdentity = CryptoPrimitives.GenerateIdentity();
        using var bobIdentity = CryptoPrimitives.GenerateIdentity();

        var alicePublic = CryptoPrimitives.ExportPublicPoint(aliceIdentity);
        var bobPublic = CryptoPrimitives.ExportPublicPoint(bobIdentity);
        var aliceId = PeerId.Of(alicePublic);
        var bobId = PeerId.Of(bobPublic);

        var aliceStore = new FileSystemBlobStore(
            Path.Combine(root, "alice"), new CacheSettings(), clock, _ => long.MaxValue);
        var bobStore = new FileSystemBlobStore(
            Path.Combine(root, "bob"), new CacheSettings(), clock, _ => long.MaxValue);

        var manifest = await FederationRun.TinyAppearanceAsync(aliceStore, ct).ConfigureAwait(false);
        var pairSecret = RandomNumberGenerator.GetBytes(32);
        IReadOnlyList<RendezvousAddress> anchor = [settings.Anchor];

        var aliceBook = new PairBook(clock);
        aliceBook.Load([FederationRun.Pair(bobId, bobPublic, pairSecret, "Bob", anchor)]);

        var bobBook = new PairBook(clock);
        bobBook.Load([FederationRun.Pair(aliceId, alicePublic, pairSecret, "Alice", anchor)]);

        // Chacun son cercle, comme deux plugins distincts qui auraient reçu la
        // même liste de l'autorité.
        var aliceCircle = new OpenCircle([ServiceConsensus.PublicPoint(key)], clock);
        var bobCircle = new OpenCircle([ServiceConsensus.PublicPoint(key)], clock);

        if (aliceCircle.Offer(document, out var why) is false || bobCircle.Offer(document, out why) is false)
        {
            Console.WriteLine($"   ÉCHEC : liste du harnais refusée ({why})");
            return false;
        }

        var engineSettings = new SyncEngineSettings { DataChannels = 4 };

        using var aliceLinks = new PeerLinkFactory(engineSettings.DataChannels + 1, new ConsoleLog("Alice"));
        using var bobLinks = new PeerLinkFactory(engineSettings.DataChannels + 1, new ConsoleLog("Bob"));

        using var polling = new CancellationTokenSource();
        var pumps = new[] { FederationRun.Poll(aliceLinks, polling.Token), FederationRun.Poll(bobLinks, polling.Token) };

        var alicePrint = PlayerFingerprint.Of("alice", 21);
        var bobPrint = PlayerFingerprint.Of("bob", 21);

        var aliceLog = new CountingLog("Alice");
        var bobLog = new CountingLog("Bob");
        var narrator = new NarratingApplicator(bobStore);

        await using var alice = new SyncEngine(
            aliceBook,
            new PeerConnector(aliceLinks, FederationRun.Endpoint(settings.Anchor), clock, aliceLog, circle: aliceCircle),
            new StaticAppearance(manifest, alicePrint), new NarratingApplicator(aliceStore),
            aliceStore, aliceId, aliceIdentity, clock, new ConsoleLog("Alice"), engineSettings);

        await using var bob = new SyncEngine(
            bobBook,
            new PeerConnector(bobLinks, FederationRun.Endpoint(settings.Anchor), clock, bobLog, circle: bobCircle),
            new StaticAppearance(null, bobPrint), narrator,
            bobStore, bobId, bobIdentity, clock, new ConsoleLog("Bob"), engineSettings);

        IReadOnlyList<VisiblePlayer> aliceSees = [new VisiblePlayer(new GameObjectRef(7, 0xB0B), bobPrint)];
        IReadOnlyList<VisiblePlayer> bobSees = [new VisiblePlayer(new GameObjectRef(4, 0xA11CE), alicePrint)];

        var deadline = DateTime.UtcNow.AddSeconds(settings.TimeoutSeconds);

        while (narrator.Applications == 0 && DateTime.UtcNow < deadline && ct.IsCancellationRequested is false)
        {
            await alice.TickAsync(aliceSees, ct).ConfigureAwait(false);
            await bob.TickAsync(bobSees, ct).ConfigureAwait(false);
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        await polling.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pumps).ConfigureAwait(false);

        var applied = narrator.Applications > 0;
        var viaOpen = aliceLog.OpenMatches + bobLog.OpenMatches > 0;
        var good = applied && viaOpen == expectOpen;

        Console.WriteLine($"   {(applied ? "apparence posée" : "aucun appariement")}, "
                        + $"{(viaOpen ? "par le cercle ouvert" : "par l'ancrage")}, "
                        + $"attendu : posée {(expectOpen ? "par le cercle ouvert" : "par l'ancrage")} → {(good ? "conforme" : "ÉCHEC")}");

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }

        return good;
    }
}
