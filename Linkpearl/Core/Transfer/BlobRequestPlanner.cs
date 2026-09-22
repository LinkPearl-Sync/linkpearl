using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Transfer;

/// <summary>Un blob à demander, et sa taille annoncée.</summary>
public sealed record BlobRequest(BlobHash Hash, long Size);

/// <summary>Ce qu'il reste à obtenir pour pouvoir appliquer un manifeste.</summary>
public sealed record TransferPlan(IReadOnlyList<BlobRequest> Missing, long MissingBytes, long CachedBytes);

/// <summary>
/// Compare un manifeste reçu au cache local.
/// </summary>
/// <remarks>
/// C'est ici que se joue l'intérêt de l'adressage par contenu : un pair revu
/// sans changement ne coûte rien, et deux pairs qui partagent une texture ne la
/// font télécharger qu'une fois. À huit cents mégaoctets par personne, c'est le
/// levier le plus rentable du projet.
/// </remarks>
public static class BlobRequestPlanner
{
    public static TransferPlan Plan(CharacterManifest manifest, IBlobStore store)
    {
        var missing = new Dictionary<BlobHash, long>();
        var cached = 0L;
        var seen = new HashSet<BlobHash>();

        foreach (var replacement in manifest.Replacements)
        {
            // Un pair peut répéter une entrée pour nous faire télécharger deux
            // fois le même contenu : on ne compte chaque empreinte qu'une fois.
            if (seen.Add(replacement.Hash) is false)
                continue;

            if (store.TryGetSize(replacement.Hash, out var size))
            {
                cached += size;
                continue;
            }

            missing[replacement.Hash] = replacement.Size;
        }

        // Du plus petit au plus grand : ce tri donne un retour visuel immédiat
        // et permet d'appliquer une partie de l'apparence plus tôt.
        var ordered = missing
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key.ToHex(), StringComparer.Ordinal)
            .Select(pair => new BlobRequest(pair.Key, pair.Value))
            .ToArray();

        return new TransferPlan(ordered, ordered.Sum(r => r.Size), cached);
    }
}
