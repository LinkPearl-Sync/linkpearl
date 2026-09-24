using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public sealed class BanListPagesTests
{
    private static readonly BanParameters Cheap = new(1000);

    private static BanEntry Entry(byte seed) => new(Enumerable.Repeat(seed, 32).ToArray(), $"motif {seed}", seed);

    [Fact]
    public void Deux_pages_d_une_meme_liste_se_fusionnent()
    {
        var salt = Enumerable.Repeat((byte)7, 32).ToArray();

        Assert.True(BanListPages.TryMerge(
            [new BanList(salt, Cheap, [Entry(1), Entry(2)]), new BanList(salt, Cheap, [Entry(3)])],
            out var merged, out var why), why);

        Assert.Equal([1, 2, 3], merged!.Entries.Select(entry => (int)entry.Hash[0]));
        Assert.Equal(salt, merged.Salt);
    }

    [Fact]
    public void Une_entree_vue_sur_deux_pages_ne_compte_qu_une_fois()
    {
        // La liste peut changer entre deux pages : une entrée glisse d'une page
        // à la suivante et arrive deux fois.
        var salt = new byte[32];

        Assert.True(BanListPages.TryMerge(
            [new BanList(salt, Cheap, [Entry(1)]), new BanList(salt, Cheap, [Entry(1), Entry(2)])],
            out var merged, out _));

        Assert.Equal(2, merged!.Entries.Count);
    }

    [Fact]
    public void Des_pages_sous_deux_sels_sont_refusees()
    {
        Assert.False(BanListPages.TryMerge(
            [new BanList(new byte[32], Cheap, [Entry(1)]), new BanList(Enumerable.Repeat((byte)1, 32).ToArray(), Cheap, [Entry(2)])],
            out _, out var why));

        Assert.Contains("sel", why);
    }

    [Fact]
    public void Des_pages_sous_deux_couts_sont_refusees()
        => Assert.False(BanListPages.TryMerge(
            [new BanList(new byte[32], Cheap, []), new BanList(new byte[32], new BanParameters(2000), [])],
            out _, out _));

    [Fact]
    public void Aucune_page_n_est_pas_une_liste()
        => Assert.False(BanListPages.TryMerge([], out _, out _));
}
