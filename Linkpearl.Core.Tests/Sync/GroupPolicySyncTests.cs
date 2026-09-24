using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Tests.Groups;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>
/// Deux moteurs réels reliés par un groupe privé, dont la politique diverge de
/// part et d'autre : c'est le moteur qui la fait converger.
/// </summary>
public sealed class GroupPolicySyncTests : IDisposable
{
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "linkpearl-politique-" + Guid.NewGuid().ToString("N"));
    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record World(SyncEngine Alice, SyncEngine Bob, GroupBook AliceGroups, GroupBook BobGroups, GroupId Group)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Alice.DisposeAsync();
            await Bob.DisposeAsync();
        }
    }

    private async Task<World> WorldAsync(bool aliceRenames = false, bool bobIsAhead = false)
    {
        var alice = CryptoPrimitives.GenerateIdentity();
        var bob = CryptoPrimitives.GenerateIdentity();
        _disposables.Add(alice);
        _disposables.Add(bob);

        var aliceId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(alice));
        var bobId = PeerId.Of(CryptoPrimitives.ExportPublicPoint(bob));

        var aliceStore = new FileSystemBlobStore(Path.Combine(_root, "alice"), new CacheSettings(), _clock, _ => long.MaxValue);
        var bobStore = new FileSystemBlobStore(Path.Combine(_root, "bob"), new CacheSettings(), _clock, _ => long.MaxValue);

        var content = Encoding.UTF8.GetBytes("une tenue de compagnie, en tout petit");
        var hash = BlobHash.OfContent(content);

        await using (var writer = await aliceStore.BeginWriteAsync(hash, content.Length, default))
        {
            await writer.WriteAsync(content, default);
            await writer.CommitAsync(default);
        }

        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [new FileReplacement(["chara/equipment/e0001/model/c0101e0001_top.mdl"], hash, content.Length)],
            string.Empty, null);

        var created = GroupGovernance.Create(
            "Compagnie", "lune", new RendezvousAddress("rdv.exemple.ch", 47900), CryptoPrimitives.ExportPublicPoint(alice), _clock.UtcNow);

        var aliceGroups = new GroupBook(_clock);
        aliceGroups.Load([created.Record]);

        var bobGroups = new GroupBook(_clock);
        bobGroups.Load([created.Record with { SigningKey = null }]);

        var v2 = GroupGovernance.Rename(created.Record, "Nouvelle", null);

        if (aliceRenames)
            aliceGroups.OfferPolicy(created.Record.Id, v2);

        if (bobIsAhead)
            bobGroups.OfferPolicy(created.Record.Id, v2);

        var point = new MeetingPoint();

        var aliceEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(manifest, AlicePrint),
            new RecordingApplicator(), aliceStore, aliceId, alice, _clock, new SilentLog(),
            groups: aliceGroups, policies: aliceGroups);

        var bobEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(null, BobPrint),
            new RecordingApplicator(), bobStore, bobId, bob, _clock, new SilentLog(),
            groups: bobGroups, policies: bobGroups);

        aliceEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(AlicePrint, [new GroupSighting(created.Record.Id, BobPrint, "Bob")], aliceGroups.All, []));

        bobEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(created.Record.Id, AlicePrint, "Alice")], bobGroups.All, []));

        return new World(aliceEngine, bobEngine, aliceGroups, bobGroups, created.Record.Id);
    }

    private async Task<bool> SettleAsync(World world, Func<bool> done, int rounds = 2000)
    {
        for (var i = 0; i < rounds; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(50));

            await world.Alice.TickAsync([], default);
            await world.Bob.TickAsync([], default);

            if (done())
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task La_plus_recente_gagne_des_deux_cotes()
    {
        // Alice est propriétaire et renomme ; Bob n'a que la version 1.
        await using var world = await WorldAsync(aliceRenames: true);

        Assert.True(await SettleAsync(world, () => world.BobGroups.Find(world.Group)!.Policy!.Version == 2),
            "Bob n'a jamais reçu la version 2");
        Assert.Equal("Nouvelle", world.BobGroups.Find(world.Group)!.Name);
        Assert.Equal(2UL, world.AliceGroups.Find(world.Group)!.Policy!.Version);
    }

    [Fact]
    public async Task Une_politique_plus_ancienne_recue_ne_remplace_pas_la_notre()
    {
        // Bob a la version 2, Alice seulement la 1 : Alice doit monter, Bob ne pas redescendre.
        await using var world = await WorldAsync(bobIsAhead: true);

        Assert.True(await SettleAsync(world, () => world.AliceGroups.Find(world.Group)!.Policy!.Version == 2),
            "Alice n'a jamais reçu la version 2");
        Assert.Equal(2UL, world.BobGroups.Find(world.Group)!.Policy!.Version);
    }

    [Fact]
    public void Une_paire_directe_ne_porte_pas_de_message_de_groupe()
    {
        var group = GroupBookTests.Group([.. Enumerable.Range(0, 32).Select(i => (byte)i)], _clock.UtcNow);
        var member = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], [group], []));

        Assert.True(SyncEngine.CarriesGroupMessages(member));
        Assert.False(SyncEngine.CarriesGroupMessages(member with { Group = null }));
    }
}
