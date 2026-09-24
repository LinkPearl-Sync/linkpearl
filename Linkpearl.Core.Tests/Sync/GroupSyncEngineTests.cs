using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Tests.Groups;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>
/// Deux moteurs réels qui ne se connaissent que par un groupe : carnets vides.
/// </summary>
public sealed class GroupSyncEngineTests : IDisposable
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "linkpearl-groupe-" + Guid.NewGuid().ToString("N"));
    private readonly MovableClock _clock = new();
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record World(
        SyncEngine Alice, SyncEngine Bob, GroupBook BobGroups, RecordingApplicator BobApplicator,
        PeerId AliceId, PeerId RuntimeId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Alice.DisposeAsync();
            await Bob.DisposeAsync();
        }
    }

    private async Task<World> WorldAsync(GroupMember? bobKnowsAlice = null)
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

        var group = GroupBookTests.Group(Secret, _clock.UtcNow);

        var aliceGroups = new GroupBook(_clock);
        aliceGroups.Load([group]);

        var bobGroups = new GroupBook(_clock);
        bobGroups.Load([bobKnowsAlice is null
            ? group
            : group with { Members = new Dictionary<PlayerFingerprint, GroupMember> { [AlicePrint] = bobKnowsAlice } }]);

        var point = new MeetingPoint();
        var bobApplicator = new RecordingApplicator();

        var aliceEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(manifest, AlicePrint),
            new RecordingApplicator(), aliceStore, aliceId, alice, _clock, new SilentLog(), groups: aliceGroups);

        var bobEngine = new SyncEngine(
            new PairBook(_clock), new MeetingDialer(point), new FixedAppearance(null, BobPrint),
            bobApplicator, bobStore, bobId, bob, _clock, new SilentLog(), groups: bobGroups);

        aliceEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(AlicePrint, [new GroupSighting(group.Id, BobPrint, "Bob")], aliceGroups.All, []));

        bobEngine.SetGroupPeers(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], bobGroups.All, []));

        var runtime = GroupDerivation.RuntimeId(GroupDerivation.MemberPairSecret(Secret, AlicePrint, BobPrint));

        return new World(aliceEngine, bobEngine, bobGroups, bobApplicator, aliceId, runtime);
    }

    private async Task<bool> SettleAsync(World world, Func<bool> done, int rounds = 2000)
    {
        IReadOnlyList<VisiblePlayer> bobSees = [new VisiblePlayer(new GameObjectRef(4, 100), AlicePrint)];

        for (var i = 0; i < rounds; i++)
        {
            // Le limiteur de débit remplit son seau à jetons depuis l'horloge
            // injectée : sans avance, il n'accorde jamais un octet et le
            // transfert reste bloqué.
            _clock.Advance(TimeSpan.FromMilliseconds(50));

            await world.Alice.TickAsync([], default);
            await world.Bob.TickAsync(bobSees, default);

            if (done())
                return true;

            await Task.Delay(10);
        }

        return false;
    }

    [Fact]
    public async Task Deux_membres_qui_se_voient_recoivent_l_apparence()
    {
        await using var world = await WorldAsync();

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0), "rien n'a été posé");

        var applied = Assert.Single(world.BobApplicator.Applied);
        Assert.Equal(world.RuntimeId, applied.Peer);
        Assert.Equal(world.AliceId, world.BobGroups.Find(GroupId.Of(Secret))!.Members[AlicePrint].Id);
    }

    [Fact]
    public async Task Une_cle_contestee_ne_pose_rien()
    {
        var impostor = new GroupMember { Fingerprint = AlicePrint, Id = PeerId.Of([2, 9, 9, 9]), DisplayName = "Alice" };

        await using var world = await WorldAsync(bobKnowsAlice: impostor);

        Assert.False(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0, rounds: 300));
    }

    [Fact]
    public async Task Un_membre_retire_du_plan_voit_son_apparence_effacee()
    {
        await using var world = await WorldAsync();

        Assert.True(await SettleAsync(world, () => world.BobApplicator.Applied.Count > 0), "rien n'a été posé");

        world.Bob.SetGroupPeers([]);

        Assert.True(
            await SettleAsync(world, () => world.BobApplicator.Removed.Contains(world.RuntimeId)),
            "l'apparence est restée après le départ du plan");
    }

    [Fact]
    public void Un_avis_de_retrait_n_arrete_qu_une_paire_du_carnet()
    {
        var group = GroupBookTests.Group(Secret, _clock.UtcNow);
        var member = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(BobPrint, [new GroupSighting(group.Id, AlicePrint, "Alice")], [group], []));

        Assert.False(SyncEngine.EndsOnUnpair(member));
        Assert.True(SyncEngine.EndsOnUnpair(member with { Group = null }));
    }
}
