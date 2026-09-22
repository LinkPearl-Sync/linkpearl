namespace Linkpearl.Core.Safety;

/// <summary>
/// Liste blanche des extensions de fichier qu'un pair peut nous faire appliquer.
/// </summary>
/// <remarks>
/// Cette liste ne referme pas la surface d'attaque : un <c>.mdl</c> ou un
/// <c>.tex</c> malformé reste parsé par du code natif du client, écrit sans
/// l'hypothèse qu'un attaquant contrôle l'entrée. Elle réduit seulement le
/// nombre de parseurs atteignables, et c'est le pairage explicite qui porte la
/// décision de confiance.
/// </remarks>
public static class ExtensionAllowList
{
    /// <summary>Apparence statique du personnage : modèles, matières, textures, squelette.</summary>
    private static readonly HashSet<string> Allowed =
    [
        ".tex", ".mdl", ".mtrl", ".sklb", ".skp", ".phyb", ".pbd", ".eid", ".imc",
    ];

    /// <summary>
    /// Ressources transitoires, chargées seulement quand elles jouent.
    /// </summary>
    /// <remarks>
    /// Hors périmètre de la v1 : elles exigent un suivi dédié, par personnage et
    /// par job. Elles sont nommées à part pour que le refus explique qu'il s'agit
    /// d'une limite connue et non d'un manifeste malformé.
    /// </remarks>
    private static readonly HashSet<string> Transient =
    [
        ".pap", ".tmb", ".avfx", ".atex", ".scd",
    ];

    public static bool IsAllowed(string gamePath, out string? rejection)
    {
        var extension = ExtensionOf(gamePath);

        if (extension is null)
        {
            rejection = "fichier sans extension";
            return false;
        }

        if (Allowed.Contains(extension))
        {
            rejection = null;
            return true;
        }

        rejection = extension switch
        {
            ".shpk" => "paquet de shader : du bytecode consommé par le pilote graphique",
            _ when Transient.Contains(extension) => $"ressource transitoire ({extension}), hors périmètre de la v1",
            _ => $"extension non autorisée ({extension})",
        };
        return false;
    }

    /// <summary>
    /// Extension du nom de fichier, ou null s'il n'y en a pas.
    /// </summary>
    /// <remarks>
    /// C'est la dernière extension qui compte, et seulement dans le dernier
    /// segment : un point dans un nom de répertoire n'en est pas une, et
    /// <c>truc.tex.exe</c> est un <c>.exe</c>.
    /// </remarks>
    private static string? ExtensionOf(string gamePath)
    {
        var fileStart = gamePath.LastIndexOf('/') + 1;
        var dot = gamePath.LastIndexOf('.');

        if (dot <= fileStart)
            return null;

        return gamePath[dot..];
    }
}
