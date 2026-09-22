using System.Text;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

/// <summary>
/// Le cache qui rend une reconstruction d'apparence abordable.
/// </summary>
/// <remarks>
/// Un redessin arrive à chaque changement de zone et à chaque changement de
/// tenue. Sans ce cache, chacun rehacherait huit cents mégaoctets.
/// </remarks>
public class FileHashCacheTests
{
    private static readonly BlobHash Hash = BlobHash.OfContent(Encoding.UTF8.GetBytes("un fichier"));

    private static FileStamp Stamp(long size = 42, long ticks = 1000)
        => new(size, new DateTimeOffset(ticks, TimeSpan.Zero));

    [Fact]
    public void Un_fichier_inchange_rend_son_empreinte()
    {
        var cache = new FileHashCache();
        cache.Remember("/mods/haut.mdl", Stamp(), Hash);

        Assert.True(cache.TryGet("/mods/haut.mdl", Stamp(), out var found));
        Assert.Equal(Hash, found);
    }

    [Fact]
    public void Un_fichier_jamais_vu_manque()
    {
        Assert.False(new FileHashCache().TryGet("/mods/inconnu.mdl", Stamp(), out _));
    }

    [Fact]
    public void Une_taille_differente_invalide_l_entree()
    {
        var cache = new FileHashCache();
        cache.Remember("/mods/haut.mdl", Stamp(size: 42), Hash);

        Assert.False(cache.TryGet("/mods/haut.mdl", Stamp(size: 43), out _));
    }

    [Fact]
    public void Une_date_differente_invalide_l_entree()
    {
        // Un fichier réécrit à la même taille est le cas qui compte : un mod
        // remplacé par une variante de même poids doit être rehaché.
        var cache = new FileHashCache();
        cache.Remember("/mods/haut.mdl", Stamp(ticks: 1000), Hash);

        Assert.False(cache.TryGet("/mods/haut.mdl", Stamp(ticks: 2000), out _));
    }

    [Fact]
    public void Une_entree_reecrite_remplace_la_precedente()
    {
        var cache = new FileHashCache();
        var other = BlobHash.OfContent(Encoding.UTF8.GetBytes("autre chose"));

        cache.Remember("/mods/haut.mdl", Stamp(ticks: 1000), Hash);
        cache.Remember("/mods/haut.mdl", Stamp(ticks: 2000), other);

        Assert.True(cache.TryGet("/mods/haut.mdl", Stamp(ticks: 2000), out var found));
        Assert.Equal(other, found);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Le_cache_est_borne()
    {
        // Sans plafond, une bibliothèque de mods qui tourne beaucoup finirait
        // par tenir en mémoire tout ce qu'elle a jamais contenu.
        var cache = new FileHashCache(max: 4);

        for (var i = 0; i < 10; i++)
            cache.Remember($"/mods/fichier{i}.mdl", Stamp(), Hash);

        Assert.True(cache.Count <= 4);
    }
}
