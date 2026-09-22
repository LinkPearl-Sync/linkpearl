using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Harness;

public sealed record FederationSettings(
    RendezvousAddress ServiceA, RendezvousAddress ServiceB, RendezvousAddress Dead, int TimeoutSeconds);

/// <summary>
/// La preuve que la fédération marche : deux clients dont les listes de
/// services ne se recouvrent que partiellement doivent quand même se trouver.
/// </summary>
/// <remarks>
/// Les deux services tournent à côté, lancés à la main : ils vivent dans un
/// autre dépôt depuis la séparation, et le harnais ne peut pas les instancier.
///
/// Le corpus est minuscule et synthétique. Ce run ne mesure rien, il répond à
/// une question binaire : se trouvent-ils ? Mesurer le débit est le travail du
/// mode faux pair, qui emploie une apparence réelle.
///
/// Il comporte volontairement un cas qui doit <b>échouer</b>. Un test incapable
/// d'échouer ne prouve rien : si deux listes disjointes se trouvaient quand
/// même, c'est que le lieu de rendez-vous ne serait pas celui qu'on croit.
/// </remarks>
public static class FederationRun
{
    public static async Task<bool> ExecuteAsync(FederationSettings settings, CancellationToken ct)
    {
        Console.WriteLine($"Service A : {settings.ServiceA}");
        Console.WriteLine($"Service B : {settings.ServiceB}");
        Console.WriteLine($"Service éteint : {settings.Dead}");
        Console.WriteLine();

        var ok = true;

        ok &= await ScenarioAsync(
            "recouvrement partiel",
            alicePlaces: [settings.ServiceA, settings.ServiceB],
            bobPlaces: [settings.ServiceB],
            expectApplied: true,
            settings, ct).ConfigureAwait(false);

        ok &= await ScenarioAsync(
            "listes disjointes",
            alicePlaces: [settings.ServiceA],
            bobPlaces: [settings.ServiceB],
            expectApplied: false,
            settings, ct).ConfigureAwait(false);

        ok &= await ScenarioAsync(
            "aucun lieu joignable",
            alicePlaces: [settings.Dead],
            bobPlaces: [settings.Dead],
            expectApplied: false,
            settings, ct).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(ok ? "Tous les cas se sont comportés comme attendu." : "ÉCHEC : voir ci-dessus.");
        return ok;
    }

    private static async Task<bool> ScenarioAsync(
        string name,
        IReadOnlyList<RendezvousAddress> alicePlaces,
        IReadOnlyList<RendezvousAddress> bobPlaces,
        bool expectApplied,
        FederationSettings settings,
        CancellationToken ct)
    {
        Console.WriteLine($"── {name} ──");
        Console.WriteLine($"   Alice sur [{string.Join(", ", alicePlaces)}], Bob sur [{string.Join(", ", bobPlaces)}]");

        var clock = new RealClock();
        var root = Path.Combine(Path.GetTempPath(), "linkpearl-federation-" + Guid.NewGuid().ToString("N"));

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

        var manifest = await TinyAppearanceAsync(aliceStore, ct).ConfigureAwait(false);

        // Le secret de paire est partagé : c'est lui qui dérive les jetons
        // d'annonce, et les deux côtés doivent tomber sur les mêmes.
        var pairSecret = RandomNumberGenerator.GetBytes(32);

        var aliceBook = new PairBook(clock);
        aliceBook.Load([Pair(bobId, bobPublic, pairSecret, "Bob", alicePlaces)]);

        var bobBook = new PairBook(clock);
        bobBook.Load([Pair(aliceId, alicePublic, pairSecret, "Alice", bobPlaces)]);

        var engineSettings = new SyncEngineSettings { DataChannels = 4 };

        using var aliceLinks = new PeerLinkFactory(engineSettings.DataChannels + 1, new ConsoleLog("Alice"));
        using var bobLinks = new PeerLinkFactory(engineSettings.DataChannels + 1, new ConsoleLog("Bob"));

        using var polling = new CancellationTokenSource();
        var pumps = new[] { Poll(aliceLinks, polling.Token), Poll(bobLinks, polling.Token) };

        var alicePrint = PlayerFingerprint.Of("alice", 21);
        var bobPrint = PlayerFingerprint.Of("bob", 21);

        var narrator = new NarratingApplicator(bobStore);

        await using var alice = new SyncEngine(
            aliceBook,
            new PeerConnector(aliceLinks, Endpoint(alicePlaces[0]), clock, new ConsoleLog("Alice")),
            new StaticAppearance(manifest, alicePrint), new NarratingApplicator(aliceStore),
            aliceStore, aliceId, aliceIdentity, clock, new ConsoleLog("Alice"), engineSettings);

        await using var bob = new SyncEngine(
            bobBook,
            new PeerConnector(bobLinks, Endpoint(bobPlaces[0]), clock, new ConsoleLog("Bob")),
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
        var good = applied == expectApplied;

        Console.WriteLine($"   {(applied ? "apparence posée" : "aucun appariement")}, "
                        + $"attendu : {(expectApplied ? "posée" : "aucun")} → {(good ? "conforme" : "ÉCHEC")}");

        if (bob.Statuses.FirstOrDefault() is { LastFailure: { } failure })
            Console.WriteLine($"   Bob dit : {failure}");

        try
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }

        return good;
    }

    /// <summary>Une apparence minuscule : ce run répond à une question binaire, pas à une mesure.</summary>
    private static async Task<CharacterManifest> TinyAppearanceAsync(IBlobStore store, CancellationToken ct)
    {
        var replacements = new List<FileReplacement>();

        for (var i = 0; i < 3; i++)
        {
            var content = Encoding.UTF8.GetBytes($"un petit fichier de mod numéro {i}");
            var hash = BlobHash.OfContent(content);

            await using (var writer = await store.BeginWriteAsync(hash, content.Length, ct).ConfigureAwait(false))
            {
                await writer.WriteAsync(content, ct).ConfigureAwait(false);
                await writer.CommitAsync(ct).ConfigureAwait(false);
            }

            replacements.Add(new FileReplacement(
                [$"chara/equipment/e000{i}/model/c0101e000{i}_top.mdl"], hash, content.Length));
        }

        return new CharacterManifest(CharacterManifest.CurrentVersion, replacements, string.Empty, null);
    }

    private static RendezvousEndpoint Endpoint(RendezvousAddress at) => new(at.Host, at.Port);

    private static PairRecord Pair(
        PeerId id, byte[] publicKey, byte[] secret, string name, IReadOnlyList<RendezvousAddress> places)
        => new()
        {
            Id = id,
            PublicKey = publicKey,
            PairSecret = secret,
            DisplayName = name,
            Rendezvous = places,
            Trust = PairTrust.Accepted,
            PairedAt = DateTimeOffset.UnixEpoch,
        };

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
