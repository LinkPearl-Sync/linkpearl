using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>Les deux trames qui transportent la liste signée du cercle ouvert.</summary>
public class ConsensusWireTests
{
    [Fact]
    public void Une_demande_de_page_fait_l_aller_retour()
    {
        Assert.True(RendezvousWire.TryReadConsensusQuery(RendezvousWire.ConsensusQuery(3), out var page));
        Assert.Equal(3, page);
    }

    [Theory]
    [InlineData(new byte[] { 0x17, 0x00 })]
    [InlineData(new byte[] { 0x17, 0x00, 0x10 })]
    [InlineData(new byte[] { 0x15, 0x00, 0x01 })]
    public void Une_demande_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadConsensusQuery(frame, out _));

    [Fact]
    public void Une_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.ConsensusPage(1, 3, new byte[] { 1, 2, 3 });

        Assert.True(RendezvousWire.TryReadConsensusPage(frame, out var page, out var pages, out var chunk, out var why), why);
        Assert.Equal(1, page);
        Assert.Equal(3, pages);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunk);
    }

    [Theory]
    [InlineData(new byte[] { 0x18, 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x18, 0x00, 0x02, 0x00, 0x02, 0x09 })]
    [InlineData(new byte[] { 0x18, 0x00, 0x00, 0x00, 0x11, 0x09 })]
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x01, 0x09 })]
    public void Une_page_hors_regles_est_refusee(byte[] frame)
    {
        Assert.False(RendezvousWire.TryReadConsensusPage(frame, out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_tranche_trop_longue_ne_s_ecrit_pas()
        => Assert.ThrowsAny<ArgumentException>(
            () => RendezvousWire.ConsensusPage(0, 1, new byte[RendezvousWire.ConsensusPageBytes + 1]));

    [Fact]
    public void Une_page_hors_du_plafond_ne_se_demande_pas()
        => Assert.ThrowsAny<ArgumentException>(() => RendezvousWire.ConsensusQuery(RendezvousWire.MaxConsensusPages));
}
