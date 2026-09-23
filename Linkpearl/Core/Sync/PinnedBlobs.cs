using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Sync;

/// <summary>Les blobs qu'une éviction ne doit pas toucher.</summary>
/// <remarks>
/// Ceux des apparences posées à l'écran (Penumbra lit leurs fichiers), de
/// celles en cours de réception (on vient de les payer), et de la nôtre (les
/// pairs nous les demandent).
/// </remarks>
public static class PinnedBlobs
{
    public static HashSet<BlobHash> Of(IEnumerable<CharacterManifest?> manifests)
    {
        var pinned = new HashSet<BlobHash>();

        foreach (var manifest in manifests)
        {
            if (manifest is null)
                continue;

            foreach (var replacement in manifest.Replacements)
                pinned.Add(replacement.Hash);
        }

        return pinned;
    }
}
