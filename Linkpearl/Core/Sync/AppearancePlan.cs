using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Sync;

/// <summary>Ce qu'il faut remettre au jeu pour poser l'apparence d'un pair.</summary>
/// <remarks>
/// La table associe un chemin de jeu au fichier local qui le remplace. Sa
/// valeur est toujours un chemin du cache, nommé par le hash recalculé chez
/// nous : <b>rien de ce qui vient du réseau ne devient un nom de fichier.</b>
/// </remarks>
public sealed record AppearancePlan(
    IReadOnlyDictionary<string, string> PathMap,
    string MetaManipulations,
    string? GlamourerState)
{
    /// <summary>Les chemins transitoires écartés parce que leur fichier est mal formé, avec la raison.</summary>
    public IReadOnlyList<string> Dropped { get; init; } = [];
}

/// <summary>
/// Traduit un manifeste reçu en une table de remplacements pour Penumbra.
/// </summary>
/// <remarks>
/// C'est le dernier endroit du noyau que la donnée d'un pair traverse avant
/// d'atteindre un autre plugin, et le seul qui décide ce qui sera réellement
/// posé. Il revalide donc entièrement le manifeste, alors qu'il l'a déjà été à
/// la réception : un contrôle à la réception protège de ce qui arrive, un
/// contrôle ici protège aussi de ce que nous aurions nous-mêmes abîmé entre
/// les deux.
/// </remarks>
public static class AppearancePlanner
{
    public static bool TryBuild(
        CharacterManifest manifest, IBlobStore store, Quotas quotas,
        out AppearancePlan? plan, out string? rejection)
    {
        plan = null;

        if (ManifestValidator.TryAccept(manifest, quotas, out var refus) is false)
        {
            rejection = refus;
            return false;
        }

        var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var dropped = new List<string>();

        foreach (var replacement in manifest.Replacements)
        {
            // Un blob absent veut dire que le cache a perdu entre le moment où
            // le transfert s'est achevé et celui-ci. On refuse tout plutôt que
            // de poser une apparence à trous : un personnage incohérent est plus
            // difficile à comprendre, pour celui qui le regarde comme pour
            // celui qui le porte, qu'un pair qui reste tel qu'il est.
            if (store.TryGetSize(replacement.Hash, out _) is false)
            {
                rejection = $"blob absent du cache au moment de poser : {replacement.Hash}";
                return false;
            }

            // La récence est rafraîchie ici et non à la réception : ce qui est à
            // l'écran est ce qu'il faut garder en dernier, et l'éviction choisit
            // sur ce critère.
            store.Touch(replacement.Hash);

            var local = store.PathFor(replacement.Hash);

            foreach (var gamePath in replacement.GamePaths)
            {
                // Un fichier transitoire est lu par du code natif du jeu : mal
                // formé, il ne l'atteint pas. Écarté seul, pour que le reste de
                // l'apparence soit posé quand même.
                if (TransientCategories.IsTransient(gamePath) && IsWellFormed(store, replacement.Hash, gamePath, out var malformed) is false)
                {
                    dropped.Add($"{gamePath} : {malformed}");
                    continue;
                }

                // Deux entrées pour un même chemin de jeu seraient une
                // contradiction : laquelle poser ? Le manifeste est refusé
                // plutôt que départagé par l'ordre d'itération, qui n'est pas
                // une décision mais un hasard.
                if (pathMap.TryGetValue(gamePath, out var already) && already != local)
                {
                    rejection = $"deux contenus pour un même chemin de jeu : {gamePath}";
                    return false;
                }

                pathMap[gamePath] = local;
            }
        }

        plan = new AppearancePlan(pathMap, manifest.MetaManipulations, manifest.GlamourerState) { Dropped = dropped };
        rejection = null;
        return true;
    }

    private static bool IsWellFormed(IBlobStore store, BlobHash hash, string gamePath, out string? rejection)
    {
        try
        {
            // Hors du thread du jeu, comme tout TryBuild : une lecture de
            // quelques octets, synchrone, suffit.
            using var content = store.OpenReadAsync(hash, CancellationToken.None).GetAwaiter().GetResult();
            return TransientFileCheck.IsWellFormed(gamePath, content, out rejection);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            rejection = $"illisible : {e.GetType().Name}";
            return false;
        }
    }
}
