using System.Security.Cryptography;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Recompose une liste de bannissement servie par pages.
/// </summary>
/// <remarks>
/// Dans le plugin seulement : le service découpe, il n'a jamais à recoller.
/// Chaque page est une liste entière sous le même sel ; des pages sous deux
/// sels viennent de deux listes différentes, dont les empreintes ne se
/// comparent pas, et les mêler ferait une liste où la moitié ne bannit
/// personne. La liste peut changer entre deux pages : une entrée qui glisse
/// arrive deux fois, et ne compte qu'une.
/// </remarks>
public static class BanListPages
{
    public static bool TryMerge(IReadOnlyList<BanList> pages, out BanList? merged, out string? rejection)
    {
        merged = null;

        if (pages.Count == 0)
        {
            rejection = "aucune page";
            return false;
        }

        var first = pages[0];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<BanEntry>();

        foreach (var page in pages)
        {
            if (CryptographicOperations.FixedTimeEquals(page.Salt, first.Salt) is false)
            {
                rejection = "pages sous des sels différents";
                return false;
            }

            if (page.Parameters != first.Parameters)
            {
                rejection = "pages sous des coûts de dérivation différents";
                return false;
            }

            foreach (var entry in page.Entries)
                if (seen.Add(Convert.ToHexStringLower(entry.Hash)))
                    entries.Add(entry);
        }

        if (entries.Count > BanList.MaxEntries)
        {
            rejection = $"liste trop longue (plafond {BanList.MaxEntries})";
            return false;
        }

        merged = new BanList(first.Salt, first.Parameters, entries);
        rejection = null;
        return true;
    }
}
