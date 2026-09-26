using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class GroupDialPlannerTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly byte[] OtherSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)(255 - i))];
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint Bob = PlayerFingerprint.Of("bob", 21);

    private readonly MovableClock _clock = new();

    private GroupRecord Group(byte[] secret) => GroupBookTests.Group(secret, _clock.UtcNow);

    [Fact]
    public void Un_membre_vu_devient_un_pair_de_groupe_identique_des_deux_cotes()
    {
        var group = Group(Secret);

        var fromAlice = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        var fromBob = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Bob, [new GroupSighting(group.Id, Alice, "Alice")], [group], []));

        Assert.Equal(fromAlice.Id, fromBob.Id);
        Assert.Equal(fromAlice.PairSecret, fromBob.PairSecret);
        Assert.Equal(new GroupOrigin(group.Id, Alice, Bob), fromAlice.Group);
        Assert.Equal(Bob, fromAlice.PinnedFingerprint);
        Assert.Equal("Bob", fromAlice.DisplayName);
        Assert.Equal(PairTrust.Accepted, fromAlice.Trust);
        Assert.Equal(group.Rendezvous, fromAlice.Rendezvous);
    }

    [Fact]
    public void Le_pair_de_groupe_porte_l_epinglage_du_membre()
    {
        var group = Group(Secret);
        var pinned = group with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bob", Id = PeerId.Of(new byte[65]) },
            },
        };

        var fresh = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));
        var known = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [pinned], []));

        Assert.False(fresh.Group!.Pinned);
        Assert.True(known.Group!.Pinned);
        Assert.False(known.Group.Public);
    }

    [Fact]
    public void On_ne_se_compose_ni_avec_soi_ni_dans_un_groupe_inconnu()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock).Plan(
            Alice,
            [new GroupSighting(group.Id, Alice, "Moi"), new GroupSighting(Group(OtherSecret).Id, Bob, "Bob")],
            [group],
            []));
    }

    [Fact]
    public void Deux_groupes_communs_donnent_un_seul_pair_celui_du_plus_petit_groupe()
    {
        var one = Group(Secret);
        var two = Group(OtherSecret);
        var smaller = one.Id.CompareTo(two.Id) < 0 ? one : two;

        var planned = Assert.Single(new GroupDialPlanner(_clock).Plan(
            Alice,
            [new GroupSighting(one.Id, Bob, "Bob"), new GroupSighting(two.Id, Bob, "Bob")],
            [one, two],
            []));

        Assert.Equal(smaller.Id, planned.Group!.Group);
    }

    [Fact]
    public void Une_paire_directe_n_est_pas_doublee()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], [Bob]));
    }

    [Fact]
    public void Un_membre_en_pause_n_est_pas_compose_et_ses_reglages_suivent()
    {
        var paused = Group(Secret) with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bob", Paused = true },
            },
        };

        Assert.Empty(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(paused.Id, Bob, "Bob")], [paused], []));

        var quiet = paused with
        {
            Members = new Dictionary<PlayerFingerprint, GroupMember>
            {
                [Bob] = new() { Fingerprint = Bob, DisplayName = "Bobby", Receive = TransientCategories.None },
            },
        };

        var planned = Assert.Single(new GroupDialPlanner(_clock)
            .Plan(Alice, [new GroupSighting(quiet.Id, Bob, "Bob")], [quiet], []));

        Assert.Equal(TransientCategories.None, planned.Receive);
        Assert.Equal("Bobby", planned.DisplayName);
    }

    [Fact]
    public void Un_membre_hors_de_vue_reste_cinq_minutes_puis_part()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);

        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        _clock.Advance(GroupDialPlanner.Linger);
        Assert.Single(planner.Plan(Alice, [], [group], []));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(planner.Plan(Alice, [], [group], []));
    }

    [Fact]
    public void Un_groupe_quitte_emporte_ses_membres_sans_attendre()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);

        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));
        Assert.Empty(planner.Plan(Alice, [], [], []));
    }

    [Fact]
    public void Au_dela_de_48_membres_les_plus_recemment_vus_passent_d_abord()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);
        var early = PlayerFingerprint.Of("parti", 21);

        planner.Plan(Alice, [new GroupSighting(group.Id, early, "Parti")], [group], []);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var crowd = Enumerable.Range(0, GroupDialPlanner.MaxSessions)
            .Select(i => new GroupSighting(group.Id, PlayerFingerprint.Of($"foule{i}", 21), $"F{i}"))
            .ToList();

        var planned = planner.Plan(Alice, crowd, [group], []);

        Assert.Equal(GroupDialPlanner.MaxSessions, planned.Count);
        Assert.DoesNotContain(planned, pair => pair.PinnedFingerprint == early);
    }

    private sealed class FixedBans(Dictionary<PlayerFingerprint, BanVerdict> verdicts) : IServiceBans
    {
        public ServiceBanStatus Status(PlayerFingerprint player)
            => verdicts.TryGetValue(player, out var verdict)
                ? new ServiceBanStatus(verdict, verdict is BanVerdict.Listed ? new ServiceBan(PublicGroupTests.Service, "triche") : null)
                : ServiceBanStatus.Clear;
    }

    [Fact]
    public void Un_joueur_liste_par_un_service_n_est_jamais_compose()
    {
        var group = Group(Secret);

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], [],
            new FixedBans(new() { [Bob] = BanVerdict.Listed })));
    }

    [Fact]
    public void Un_joueur_liste_en_cours_de_route_perd_sa_place()
    {
        var group = Group(Secret);
        var planner = new GroupDialPlanner(_clock);
        Assert.Single(planner.Plan(Alice, [new GroupSighting(group.Id, Bob, "Bob")], [group], []));

        Assert.Empty(planner.Plan(Alice, [], [group], [], new FixedBans(new() { [Bob] = BanVerdict.Listed })));
    }

    [Fact]
    public void En_attente_de_verdict_Public_attend_mais_un_groupe_prive_compose()
    {
        var prive = Group(Secret);
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);
        var bans = new FixedBans(new() { [Bob] = BanVerdict.Pending });

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], [], bans));
        Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(prive.Id, Bob, "Bob")], [prive], [], bans));
    }

    [Fact]
    public void Un_membre_bloque_n_est_pas_compose()
    {
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow) with { Blocked = [new GroupBan(null, Bob)] };

        Assert.Empty(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], []));
    }

    [Fact]
    public void Un_membre_du_Public_arrive_effets_coupes()
    {
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);

        var planned = Assert.Single(new GroupDialPlanner(_clock).Plan(Alice, [new GroupSighting(@public.Id, Bob, "Bob")], [@public], []));

        Assert.Equal(TransientCategories.None, planned.Receive);
    }

    /// <summary>Un groupe privé dont l'identifiant passe après celui du Public.</summary>
    private GroupRecord PrivateAfterPublic()
    {
        for (byte seed = 0; ; seed++)
        {
            var group = Group([.. Enumerable.Range(0, 32).Select(i => (byte)(i + seed))]);

            if (group.Id.CompareTo(PublicGroup.Id) > 0)
                return group;
        }
    }

    [Fact]
    public void Un_groupe_prive_commun_l_emporte_sur_le_Public()
    {
        // Sinon un ami du groupe arriverait par le Public, effets coupés.
        var prive = PrivateAfterPublic();
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);

        var planned = Assert.Single(new GroupDialPlanner(_clock).Plan(
            Alice, [new GroupSighting(@public.Id, Bob, "Bob"), new GroupSighting(prive.Id, Bob, "Bob")], [@public, prive], []));

        Assert.Equal(prive.Id, planned.Group!.Group);
    }

    [Fact]
    public void Un_blocage_dans_le_Public_ne_casse_pas_le_groupe_prive_commun()
    {
        // Alice a bloqué Bob dans le Public, pas Bob : les deux doivent quand
        // même choisir le même groupe, sans quoi leurs secrets de paire
        // diffèrent et aucune session ne s'ouvre plus jamais.
        var prive = PrivateAfterPublic();
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);

        var fromAlice = Assert.Single(new GroupDialPlanner(_clock).Plan(
            Alice, [new GroupSighting(@public.Id, Bob, "Bob"), new GroupSighting(prive.Id, Bob, "Bob")],
            [@public with { Blocked = [new GroupBan(null, Bob)] }, prive], []));

        var fromBob = Assert.Single(new GroupDialPlanner(_clock).Plan(
            Bob, [new GroupSighting(@public.Id, Alice, "Alice"), new GroupSighting(prive.Id, Alice, "Alice")],
            [@public, prive], []));

        Assert.Equal(fromAlice.PairSecret, fromBob.PairSecret);
    }

    [Fact]
    public void Un_verdict_qui_repasse_en_attente_ne_coupe_pas_un_membre_du_Public_deja_compose()
    {
        // Une liste qui reçoit sa première entrée remet tout le monde en
        // attente : couper tout le Public d'un coup ferait clignoter un lieu
        // entier. En attente refuse une nouvelle composition, pas une en cours.
        var @public = PublicGroup.Create([PublicGroupTests.Service], _clock.UtcNow);
        var planner = new GroupDialPlanner(_clock);
        IReadOnlyList<GroupSighting> seen = [new GroupSighting(@public.Id, Bob, "Bob")];

        Assert.Single(planner.Plan(Alice, seen, [@public], []));

        Assert.Single(planner.Plan(Alice, seen, [@public], [], new FixedBans(new() { [Bob] = BanVerdict.Pending })));
    }
}
