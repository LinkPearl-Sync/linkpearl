using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>Le hachage de rendez-vous qui désigne les deux services d'une paire.</summary>
public class ServicePlacementTests
{
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    private static ConsensusEntry Entry(string address, byte family)
        => new(address, "", [.. Enumerable.Repeat(family, 8)]);

    private static string[] Hosts(IReadOnlyList<RendezvousAddress> chosen)
        => [.. chosen.Select(ServiceConsensus.Canonical)];

    [Fact]
    public void Le_score_est_fige()
        => Assert.Equal(
            "82cc9af59e38373519a29f7ecd2ff72b3dd4eefdc6bde0893dd63b9dbea01967",
            Convert.ToHexStringLower(ServicePlacement.Score(Secret, "rdv.a.ch:47900")));

    [Fact]
    public void Les_deux_meilleurs_scores_sont_retenus()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.d.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Deux_services_d_une_meme_famille_ne_sont_jamais_retenus_ensemble()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 1), Entry("rdv.c.ch:443", 2), Entry("rdv.d.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.c.ch:443" }, Hosts(chosen));
    }

    [Fact]
    public void Une_seule_famille_ne_donne_qu_un_service()
    {
        var chosen = ServicePlacement.Choose(Secret, [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Un_service_sans_rapport_qui_sort_ne_deplace_pas_la_paire()
    {
        ConsensusEntry[] before = [Entry("rdv.a.ch:47900", 1), Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)];
        ConsensusEntry[] after = [Entry("rdv.b.ch:47900", 2), Entry("rdv.c.ch:443", 3), Entry("rdv.d.ch:47900", 4)];

        Assert.Equal(Hosts(ServicePlacement.Choose(Secret, before)), Hosts(ServicePlacement.Choose(Secret, after)));
    }

    [Fact]
    public void La_casse_ne_change_pas_la_place()
    {
        var chosen = ServicePlacement.Choose(Secret,
            [Entry("RDV.B.CH:47900", 2), Entry("rdv.d.ch", 4), Entry("rdv.a.ch:47900", 1)]);

        Assert.Equal(new[] { "rdv.b.ch:47900", "rdv.d.ch:47900" }, Hosts(chosen));
    }

    [Fact]
    public void Une_liste_vide_ne_donne_rien()
        => Assert.Empty(ServicePlacement.Choose(Secret, []));
}
