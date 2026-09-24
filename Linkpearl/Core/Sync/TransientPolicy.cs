using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Sync;

/// <summary>
/// Retire d'un manifeste reçu les animations, VFX ou sons qu'on refuse de ce pair.
/// </summary>
/// <remarks>
/// Appliqué avant le plan de téléchargement : ce qui est bloqué n'est ni reçu
/// ni posé. L'émetteur envoie le même manifeste à tous, et c'est le receveur
/// qui choisit ce qu'il accepte, comme chez les clients comparables.
///
/// Un chemin bloqué est retiré de son entrée ; une entrée qui n'a plus aucun
/// chemin disparaît. Quand rien n'est retiré, la même instance est rendue : le
/// moteur compare les manifestes, et une copie identique ne doit rien changer.
/// </remarks>
public static class TransientPolicy
{
    public static CharacterManifest Filter(CharacterManifest manifest, TransientCategories allowed)
    {
        if (allowed == TransientCategories.All)
            return manifest;

        var kept = new List<FileReplacement>(manifest.Replacements.Count);
        var changed = false;

        foreach (var replacement in manifest.Replacements)
        {
            var paths = replacement.GamePaths.Where(allowed.Allows).ToList();

            if (paths.Count == replacement.GamePaths.Count)
            {
                kept.Add(replacement);
                continue;
            }

            changed = true;

            if (paths.Count > 0)
                kept.Add(replacement with { GamePaths = paths });
        }

        var swaps = manifest.SwapsOrNone.Where(s => allowed.Allows(s.GamePath)).ToList();
        var swapsChanged = swaps.Count != manifest.SwapsOrNone.Count;

        if (changed is false && swapsChanged is false)
            return manifest;

        return manifest with
        {
            Replacements = changed ? kept : manifest.Replacements,
            Swaps = swapsChanged ? (swaps.Count > 0 ? swaps : null) : manifest.Swaps,
        };
    }
}
