namespace Linkpearl.Core.Manifest;

/// <summary>
/// Lit un chemin résolu par Penumbra pour une animation, un VFX ou un son.
/// </summary>
/// <remarks>
/// Penumbra préfixe certains chemins résolus d'un marqueur de collection entre
/// barres verticales, pour savoir plus tard à qui appartient la ressource
/// chargée. Ce marqueur n'est pas un chemin : le garder ferait chercher un
/// fichier qui n'existe pas.
/// </remarks>
public static class TransientPath
{
    public static string WithoutPenumbraPrefix(string resolved)
    {
        if (resolved.Length == 0 || resolved[0] != '|')
            return resolved;

        var end = resolved.IndexOf('|', 1);
        return end < 0 ? resolved : resolved[(end + 1)..];
    }

    /// <summary>Vrai si Penumbra redirige ce chemin de jeu, vers un fichier ou un autre chemin de jeu.</summary>
    /// <remarks>
    /// Un échange vers un autre chemin de jeu compte : c'est la forme de bien
    /// des mods de pose, qui font jouer une animation existante à la place
    /// d'une autre. Le classement des ressources en fait un échange sans
    /// fichier à transférer.
    /// </remarks>
    public static bool TryModded(string gamePath, string resolved, out string actualPath)
    {
        actualPath = WithoutPenumbraPrefix(resolved);

        if (actualPath.Length == 0)
            return false;

        // Lettre de lecteur ou racine : un fichier du disque. Pas le seul
        // séparateur Windows, qu'un chemin de jeu peut aussi porter.
        if (actualPath[0] is '/' or '\\' || actualPath.Length >= 2 && actualPath[1] == ':')
            return true;

        return string.Equals(Normalize(actualPath), Normalize(gamePath), StringComparison.Ordinal) is false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();
}
