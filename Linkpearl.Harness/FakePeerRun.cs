using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Channels;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Core.Transport;

namespace Linkpearl.Harness;

public sealed record FakePeerSettings(
    string SourceCacheRoot, string ManifestPath, bool SynthesizeFromCache,
    int DataChannels, int BlockSize, bool RateLimited, int TimeoutSeconds,
    int LatencyMs = 0, double LossPercent = 0);

/// <summary>Un journal qui parle sur la console.</summary>
internal sealed class ConsoleLog(string who) : ILogSink
{
    public void Debug(string message)
    {
    }

    public void Info(string message) => Console.WriteLine($"  [{who}] {message}");

    public void Warning(string message, Exception? exception = null)
        => Console.WriteLine($"  [{who}] {message}{(exception is null ? "" : $" ({exception.Message})")}");
}

/// <summary>Ce qu'un côté montre de lui, figé pour la durée du run.</summary>
internal sealed class StaticAppearance(CharacterManifest? manifest, PlayerFingerprint? fingerprint) : ILocalAppearance
{
    public PlayerFingerprint? Fingerprint { get; } = fingerprint;

    public Task<CharacterManifest?> CurrentAsync(CancellationToken ct) => Task.FromResult(manifest);
}

/// <summary>
/// Un applicateur qui raconte au lieu de poser.
/// </summary>
/// <remarks>
/// Le harnais n'a pas de jeu sous la main. Ce qu'il vérifie est tout ce qui
/// précède : que le moteur décide de poser, au bon moment, avec un plan que
/// Penumbra accepterait. La pose elle-même est éprouvée en jeu, et nulle part
/// ailleurs.
/// </remarks>
internal sealed class NarratingApplicator(IBlobStore store) : IRemoteApplicator
{
    public int Applications { get; private set; }

    public int Removals { get; private set; }

    public string? Failure { get; private set; }

    public Task<bool> ApplyAsync(PeerId peer, GameObjectRef target, CharacterManifest manifest, CancellationToken ct)
    {
        Applications++;

        // Le même plan que celui qui partirait vers Penumbra : c'est lui qui
        // dirait qu'un blob manque ou qu'un chemin est refusé.
        if (AppearancePlanner.TryBuild(manifest, store, Quotas.Default, out var plan, out var why) is false)
        {
            Failure = why;
            Console.WriteLine($"  Plan refusé : {why}");
            return Task.FromResult(true);
        }

        Console.WriteLine($"  Apparence posée sur l'objet {target.ObjectIndex} : "
                        + $"{plan!.PathMap.Count} chemins de jeu, "
                        + $"{plan.PathMap.Values.Distinct().Count()} fichiers distincts, "
                        + $"état Glamourer {(plan.GlamourerState is null ? "absent" : "présent")}.");

        return Task.FromResult(true);
    }

    public Task<bool> ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct)
    {
        Console.WriteLine($"  Extras posés sans redessin : {change}.");
        return Task.FromResult(true);
    }

    public Task RemoveAsync(PeerId peer, CancellationToken ct)
    {
        Removals++;
        Console.WriteLine("  Apparence retirée.");
        return Task.CompletedTask;
    }

    public bool CanApply(out string reason)
    {
        reason = string.Empty;
        return true;
    }
}

/// <summary>
/// Joint un pair en boucle locale : un côté compose, l'autre décroche.
/// </summary>
/// <remarks>
/// Asymétrique, alors que le moteur laisse les deux côtés tenter. En vrai,
/// c'est le rendez-vous qui les départage en ne rendant l'adresse qu'une fois
/// les deux annoncés. Ici il n'y a pas de rendez-vous, et deux composeurs
/// simultanés ouvriraient deux liens pour une seule paire.
/// </remarks>
internal sealed class LoopbackDialer : IPeerDialer
{
    private readonly PeerLinkFactory _links;
    private readonly int? _dialPort;
    private readonly Channel<IPeerLink> _accepted = Channel.CreateUnbounded<IPeerLink>();

    public LoopbackDialer(PeerLinkFactory links, int? dialPort)
    {
        _links = links;
        _dialPort = dialPort;

        _links.Accepted += (_, link) => _accepted.Writer.TryWrite(link);
    }

    public async Task<ConnectionAttempt> ConnectAsync(PairRecord pair, CancellationToken ct)
    {
        // Le jeton d'abord, des deux côtés : sans lui, la fabrique refuse la
        // connexion entrante sans rien révéler.
        var token = PeerConnector.TokenFor(pair.PairSecret);
        _links.Allow(token, pair.DisplayName);

        if (_dialPort is not { } port)
        {
            var incoming = await _accepted.Reader.ReadAsync(ct).ConfigureAwait(false);
            return new ConnectionAttempt(incoming, false, null);
        }

        var link = await _links
            .ConnectAsync([new IPEndPoint(IPAddress.Loopback, port)], token, TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);

        return link is null
            ? new ConnectionAttempt(null, false, "personne n'a répondu en boucle locale")
            : new ConnectionAttempt(link, false, null);
    }
}

/// <summary>
/// Un pair pour de faux, une connexion pour de vrai.
/// </summary>
/// <remarks>
/// Deux moteurs complets, chacun avec son carnet, son identité et son cache,
/// reliés par LiteNetLib sur la boucle locale. Le handshake, le manifeste, le
/// transfert et la décision de poser sont les vrais : seuls le jeu et le
/// rendez-vous manquent.
///
/// C'est ce qui permet d'éprouver la chaîne sans attendre qu'une deuxième
/// personne soit disponible, et c'est aussi ce qui dit, avant le jeu, combien
/// de temps prend une apparence réelle.
/// </remarks>
public static class FakePeerRun
{
    /// <remarks>
    /// Le cache de destination est supprimé à la fin, quoi qu'il arrive. Sous
    /// WSL, /tmp vit en mémoire : chaque essai y laissait quatre cents
    /// mégaoctets, et une série de mesures finissait par le remplir, faussant
    /// les suivantes (un passage à seize canaux a pris 89 s au lieu de 12).
    /// </remarks>
    public static async Task<bool> ExecuteAsync(FakePeerSettings settings, CancellationToken ct)
    {
        var destinationRoot = Path.Combine(Path.GetTempPath(), "linkpearl-faux-pair-" + Guid.NewGuid().ToString("N"));

        try
        {
            return await RunAsync(settings, destinationRoot, ct).ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(destinationRoot))
                Directory.Delete(destinationRoot, recursive: true);
        }
    }

    private static async Task<bool> RunAsync(FakePeerSettings settings, string destinationRoot, CancellationToken ct)
    {
        var clock = new RealClock();

        using var aliceIdentity = CryptoPrimitives.GenerateIdentity();
        using var bobIdentity = CryptoPrimitives.GenerateIdentity();

        var alicePublic = CryptoPrimitives.ExportPublicPoint(aliceIdentity);
        var bobPublic = CryptoPrimitives.ExportPublicPoint(bobIdentity);
        var aliceId = PeerId.Of(alicePublic);
        var bobId = PeerId.Of(bobPublic);

        var source = new FileSystemBlobStore(
            settings.SourceCacheRoot, new CacheSettings(), clock, _ => long.MaxValue);

        var destination = new FileSystemBlobStore(
            destinationRoot, new CacheSettings(), clock, _ => long.MaxValue);

        var manifest = await LoadManifestAsync(settings, ct).ConfigureAwait(false);

        if (manifest is null)
            return false;

        var total = manifest.Replacements.DistinctBy(r => r.Hash).Sum(r => r.Size);

        Console.WriteLine($"Cache source      : {settings.SourceCacheRoot}");
        Console.WriteLine($"  {source.Count} blobs, {source.TotalBytes / 1024.0 / 1024.0:F1} Mo");
        Console.WriteLine($"Cache destination : {destinationRoot} (vide)");
        Console.WriteLine($"Manifeste         : {manifest.Replacements.Count} entrées, "
                        + $"{manifest.Replacements.Sum(r => r.GamePaths.Count)} chemins de jeu, "
                        + $"{total / 1024.0 / 1024.0:F1} Mo de contenu distinct.");
        Console.WriteLine();

        // Le secret de paire est partagé : c'est lui qui dérive le jeton de
        // connexion, et les deux côtés doivent tomber sur le même.
        var pairSecret = RandomNumberGenerator.GetBytes(32);

        var aliceBook = new PairBook(clock);
        aliceBook.Load([Pair(bobId, bobPublic, pairSecret, "Bob")]);

        var bobBook = new PairBook(clock);
        bobBook.Load([Pair(aliceId, alicePublic, pairSecret, "Alice")]);

        var channels = settings.DataChannels + 1;

        using var aliceLinks = new PeerLinkFactory(channels, new ConsoleLog("Alice"));
        using var bobLinks = new PeerLinkFactory(channels, new ConsoleLog("Bob"));

        // Un relais qui retarde et perd, entre Bob et Alice, pour mesurer le
        // moteur tel qu'il vivra entre deux foyers et non sur une seule machine.
        using var proxy = settings.LatencyMs > 0 || settings.LossPercent > 0
            ? new ImpairmentProxy(
                new IPEndPoint(IPAddress.Loopback, aliceLinks.LocalPort),
                settings.LatencyMs, 0, settings.LossPercent, seed: 7)
            : null;

        using var polling = new CancellationTokenSource();
        var pumps = new[] { Poll(aliceLinks, polling.Token), Poll(bobLinks, polling.Token) };

        var alicePrint = PlayerFingerprint.Of("alice", 21);
        var bobPrint = PlayerFingerprint.Of("bob", 21);

        var engineSettings = new SyncEngineSettings
        {
            DataChannels = settings.DataChannels,
            BlockSize = settings.BlockSize,
            Limiter = settings.RateLimited
                ? new RateLimiterSettings()
                : new RateLimiterSettings
                {
                    InitialBytesPerSecond = 512L * 1024 * 1024,
                    CeilingBytesPerSecond = 512L * 1024 * 1024,
                },
        };

        var narrator = new NarratingApplicator(destination);

        await using var alice = new SyncEngine(
            aliceBook, new LoopbackDialer(aliceLinks, dialPort: null),
            new StaticAppearance(manifest, alicePrint), new NarratingApplicator(source),
            source, aliceId, aliceIdentity, clock, new ConsoleLog("Alice"), engineSettings);

        await using var bob = new SyncEngine(
            bobBook, new LoopbackDialer(bobLinks, proxy?.Port ?? aliceLinks.LocalPort),
            new StaticAppearance(null, bobPrint), narrator,
            destination, bobId, bobIdentity, clock, new ConsoleLog("Bob"), engineSettings);

        IReadOnlyList<VisiblePlayer> aliceSees = [new VisiblePlayer(new GameObjectRef(7, 0xB0B), bobPrint)];
        IReadOnlyList<VisiblePlayer> bobSees = [new VisiblePlayer(new GameObjectRef(4, 0xA11CE), alicePrint)];

        Console.WriteLine($"Boucle locale : Alice écoute sur {aliceLinks.LocalPort}, Bob compose.");
        Console.WriteLine();

        var watch = Stopwatch.StartNew();
        var deadline = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        var lastReport = TimeSpan.Zero;

        while (narrator.Applications == 0 && watch.Elapsed < deadline && ct.IsCancellationRequested is false)
        {
            await alice.TickAsync(aliceSees, ct).ConfigureAwait(false);
            await bob.TickAsync(bobSees, ct).ConfigureAwait(false);

            if (watch.Elapsed - lastReport > TimeSpan.FromSeconds(2))
            {
                lastReport = watch.Elapsed;
                Report(bob, watch.Elapsed);
            }

            await Task.Delay(20, ct).ConfigureAwait(false);
        }

        watch.Stop();
        await polling.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pumps).ConfigureAwait(false);

        Console.WriteLine();

        if (narrator.Applications == 0)
        {
            Console.WriteLine($"Rien n'a été posé en {watch.Elapsed.TotalSeconds:F0} s.");
            Report(bob, watch.Elapsed);
            return false;
        }

        var seconds = watch.Elapsed.TotalSeconds;

        Console.WriteLine($"Apparence complète en {seconds:F1} s, "
                        + $"{total / 1024.0 / 1024.0 / Math.Max(seconds, 0.001):F1} Mo/s.");

        // Le cache de destination doit porter exactement ce que le manifeste
        // annonce : le transfert ne vaut que s'il est vérifiable après coup.
        var missing = manifest.Replacements
            .Select(r => r.Hash)
            .Distinct()
            .Where(hash => destination.TryGetSize(hash, out _) is false)
            .ToList();

        Console.WriteLine(missing.Count == 0
            ? $"Cache vérifié : {destination.Count} blobs, {destination.TotalBytes / 1024.0 / 1024.0:F1} Mo."
            : $"ÉCHEC : {missing.Count} blobs annoncés mais absents du cache de destination.");

        return missing.Count == 0 && narrator.Failure is null;
    }

    private static void Report(SyncEngine bob, TimeSpan elapsed)
    {
        if (bob.Statuses.FirstOrDefault() is not { } status)
            return;

        var view = status.View;

        Console.WriteLine($"  {elapsed.TotalSeconds,5:F0} s  {status.State,-12} "
                        + $"{view.ReceivedBytes / 1024.0 / 1024.0,7:F1} / {view.MissingBytes / 1024.0 / 1024.0,-7:F1} Mo"
                        + $"{(view.Ready ? "  prêt" : "")}");
    }

    private static async Task<CharacterManifest?> LoadManifestAsync(FakePeerSettings settings, CancellationToken ct)
    {
        if (settings.SynthesizeFromCache)
        {
            Console.WriteLine("Manifeste SYNTHÉTIQUE, reconstruit depuis les blobs du cache source.");
            return EndToEndRun.SynthesizeFromCache(settings.SourceCacheRoot);
        }

        if (File.Exists(settings.ManifestPath) is false)
        {
            Console.WriteLine($"Manifeste introuvable : {settings.ManifestPath}");
            Console.WriteLine("Faire une capture en jeu avec /lpearl capture, ou passer --from-cache.");
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(settings.ManifestPath, ct).ConfigureAwait(false);

        if (ManifestCodec.TryDecompress(bytes, Quotas.Default, out var manifest, out var why) is false)
        {
            Console.WriteLine($"Manifeste illisible : {why}");
            return null;
        }

        if (manifest!.Replacements.Count != 0)
            return manifest;

        Console.WriteLine("Le manifeste est vide : la capture a probablement été faite avec les mods");
        Console.WriteLine("désactivés. Relancer /lpearl capture mods activés, ou utiliser --from-cache.");
        return null;
    }

    private static PairRecord Pair(PeerId id, byte[] publicKey, byte[] secret, string name)
        => new()
        {
            Id = id,
            PublicKey = publicKey,
            PairSecret = secret,
            DisplayName = name,
            Rendezvous = [new RendezvousAddress("127.0.0.1", 47900)],
            Trust = PairTrust.Accepted,
            PairedAt = DateTimeOffset.UnixEpoch,
        };

    /// <summary>
    /// Bat la socket, comme le ferait le thread du jeu.
    /// </summary>
    /// <remarks>
    /// Quinze millisecondes, soit la cadence d'une image : c'est celle que le
    /// plugin tiendra depuis Framework.Update, et mesurer avec une autre
    /// donnerait un débit que le jeu ne reproduirait pas.
    /// </remarks>
    private static Task Poll(PeerLinkFactory links, CancellationToken ct)
        => Task.Run(async () =>
        {
            while (ct.IsCancellationRequested is false)
            {
                links.Poll();

                try
                {
                    await Task.Delay(15, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }, CancellationToken.None);
}
