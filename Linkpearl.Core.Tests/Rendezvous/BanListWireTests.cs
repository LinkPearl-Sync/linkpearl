using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

public sealed class BanListWireTests
{
    [Fact]
    public void Une_demande_de_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.BanListQuery(3);

        Assert.True(RendezvousWire.TryReadBanListQuery(frame, out var page));
        Assert.Equal(3, page);
    }

    [Theory]
    [InlineData(new byte[] { 0x15, 0x00 })]
    [InlineData(new byte[] { 0x15, 0x00, 0x40 })]          // page 64, hors bornes
    [InlineData(new byte[] { 0x16, 0x00, 0x01 })]          // mauvais type
    [InlineData(new byte[] { 0x15, 0x00, 0x01, 0x00 })]    // un octet de trop
    public void Une_demande_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadBanListQuery(frame, out _));

    [Fact]
    public void Une_page_fait_l_aller_retour()
    {
        var frame = RendezvousWire.BanListData(1, 3, "{\"version\":1}");

        Assert.True(RendezvousWire.TryReadBanListData(frame, out var page, out var pages, out var json, out var why), why);
        Assert.Equal((1, 3, "{\"version\":1}"), (page, pages, json));
    }

    [Theory]
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00 })]                  // tronquée
    [InlineData(new byte[] { 0x16, 0x00, 0x02, 0x00, 0x02, 0x7b })]      // page 2 sur 2
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x00, 0x7b })]      // zéro page
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x41, 0x7b })]      // 65 pages
    [InlineData(new byte[] { 0x16, 0x00, 0x00, 0x00, 0x01 })]            // sans texte
    public void Une_page_hors_regles_est_refusee(byte[] frame)
        => Assert.False(RendezvousWire.TryReadBanListData(frame, out _, out _, out _, out _));

    [Fact]
    public void Une_page_pleine_au_pire_tient_dans_une_trame()
    {
        // Le pire motif : 120 caractères que l'encodeur JSON par défaut échappe
        // chacun en une séquence de six. C'est ce qui fixe 64 entrées par page.
        var reason = new string('<', BanList.MaxReasonLength);
        var entries = Enumerable.Range(0, RendezvousWire.BanListPageEntries)
            .Select(i => new BanEntry(Enumerable.Repeat((byte)i, 32).ToArray(), reason, long.MaxValue))
            .ToList();

        var json = new BanList(new byte[32], BanParameters.Default, entries).ToJson(long.MaxValue);
        var frame = RendezvousWire.BanListData(0, RendezvousWire.MaxBanListPages, json);

        Assert.True(frame.Length <= RendezvousWire.MaxFrameLength, $"{frame.Length} octets");
    }

    [Fact]
    public void Toutes_les_entrees_possibles_tiennent_dans_les_pages()
        => Assert.True(RendezvousWire.BanListPageEntries * RendezvousWire.MaxBanListPages >= BanList.MaxEntries);
}
