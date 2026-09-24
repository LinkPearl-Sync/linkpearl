using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Safety;

/// <summary>
/// Valide un échange : un chemin de jeu que Penumbra fera servir par un autre.
/// </summary>
/// <remarks>
/// Les deux côtés passent les mêmes contrôles qu'un chemin de fichier, puisque
/// la cible aussi atteint Penumbra. S'y ajoute une règle propre aux échanges :
/// même extension des deux côtés. Faire lire un modèle là où le jeu attend une
/// animation reviendrait à donner à du code natif un format qu'il n'attend
/// pas, et aucun mod honnête n'en a besoin.
/// </remarks>
public static class FileSwapPolicy
{
    public static bool TryNormalize(FileSwap swap, Quotas quotas, out FileSwap? normalized, out string? rejection)
    {
        normalized = null;

        if (TryNormalizeSide(swap.GamePath, quotas, out var gamePath, out rejection) is false
            || TryNormalizeSide(swap.TargetGamePath, quotas, out var target, out rejection) is false)
            return false;

        if (string.Equals(Extension(gamePath), Extension(target), StringComparison.Ordinal) is false)
        {
            rejection = "échange entre deux extensions différentes";
            return false;
        }

        normalized = new FileSwap(gamePath, target);
        rejection = null;
        return true;
    }

    private static bool TryNormalizeSide(string raw, Quotas quotas, out string normalized, out string? rejection)
    {
        if (GamePathPolicy.TryNormalize(raw, quotas, out normalized, out rejection) is false)
            return false;

        return ExtensionAllowList.IsAllowed(normalized, out rejection);
    }

    private static string Extension(string gamePath)
    {
        var dot = gamePath.LastIndexOf('.');
        return dot < 0 ? string.Empty : gamePath[dot..];
    }
}
