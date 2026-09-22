namespace Linkpearl.Core.Safety;

/// <summary>
/// Valide et normalise un chemin de jeu venant d'un pair.
/// </summary>
/// <remarks>
/// Le chemin de jeu est la clé du dictionnaire que l'on passe à Penumbra. C'est
/// la seule chaîne reçue du réseau qui atteigne un autre plugin, donc la seule
/// qui puisse lui faire désigner autre chose que ce qu'on croit.
///
/// La normalisation se limite à la casse. En particulier l'antislash n'est
/// jamais traduit en barre oblique : traduire reviendrait à valider une chaîne
/// et à en utiliser une autre, ce qui est exactement la forme que prennent les
/// contournements de ce genre de contrôle.
/// </remarks>
public static class GamePathPolicy
{
    /// <summary>
    /// Racines réelles de l'arborescence du jeu.
    /// </summary>
    /// <remarks>
    /// Liste close, en défense de profondeur : c'est la liste blanche
    /// d'extensions qui écarte vraiment ce qu'un mod n'a pas à remplacer, comme
    /// les tables <c>exd</c> ou les scripts.
    /// </remarks>
    private static readonly HashSet<string> KnownRoots =
    [
        "bg", "bgcommon", "chara", "common", "cut", "exd", "game_script",
        "music", "shader", "sound", "ui", "ui_script", "vfx",
    ];

    /// <summary>
    /// Rend vrai et un chemin normalisé si le chemin est acceptable, faux et une
    /// raison sinon. La raison part dans le journal : sans elle, un manifeste
    /// refusé serait indiagnosticable à distance.
    /// </summary>
    public static bool TryNormalize(string? raw, Quotas quotas, out string normalized, out string? rejection)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            rejection = "chemin vide";
            return false;
        }

        if (raw.Length > quotas.MaxGamePathLength)
        {
            rejection = $"chemin trop long ({raw.Length} caractères, maximum {quotas.MaxGamePathLength})";
            return false;
        }

        // La casse d'abord : Penumbra peut rendre des chemins en casse mixte, et
        // normaliser rend aussi le hash du manifeste stable d'une machine à l'autre.
        var path = raw.ToLowerInvariant();

        foreach (var c in path)
        {
            if (!IsAllowed(c))
            {
                // Le caractère fautif n'est pas repris tel quel dans la raison :
                // il vient du réseau et finirait dans un journal.
                rejection = $"caractère interdit en position {path.IndexOf(c)} (point de code U+{(int)c:X4})";
                return false;
            }
        }

        if (path[0] == '/')
        {
            rejection = "chemin absolu";
            return false;
        }

        if (path[^1] == '/')
        {
            rejection = "barre oblique finale";
            return false;
        }

        var segments = path.Split('/');

        if (segments.Length > quotas.MaxGamePathDepth)
        {
            rejection = $"chemin trop profond ({segments.Length} segments, maximum {quotas.MaxGamePathDepth})";
            return false;
        }

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case "":
                    rejection = "segment vide";
                    return false;
                case ".":
                    rejection = "segment « . »";
                    return false;
                case "..":
                    rejection = "traversée de répertoire";
                    return false;
            }
        }

        if (!KnownRoots.Contains(segments[0]))
        {
            rejection = "racine inconnue de l'arborescence du jeu";
            return false;
        }

        normalized = path;
        rejection = null;
        return true;
    }

    /// <summary>
    /// Jeu de caractères des chemins du jeu, une fois la casse abaissée.
    /// </summary>
    /// <remarks>
    /// Volontairement restreint à l'ASCII : il écarte d'un coup l'antislash, le
    /// deux-points des lettres de lecteur et des flux alternatifs NTFS, l'octet
    /// nul, les blancs, et les homoglyphes unicode.
    /// </remarks>
    private static bool IsAllowed(char c)
        => c is >= 'a' and <= 'z'
        || c is >= '0' and <= '9'
        || c is '_' or '/' or '.' or '-';
}
