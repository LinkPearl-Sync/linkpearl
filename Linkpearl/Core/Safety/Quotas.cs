namespace Linkpearl.Core.Safety;

/// <summary>
/// Plafonds appliqués à toute donnée venant d'un pair.
/// </summary>
/// <remarks>
/// Un dépassement coupe la session plutôt que de tronquer : une donnée tronquée
/// donne un personnage incohérent et masque une tentative d'abus.
/// </remarks>
public sealed record Quotas
{
    public static Quotas Default { get; } = new();

    /// <summary>Nombre de remplacements de fichiers dans un manifeste.</summary>
    public int MaxReplacements { get; init; } = 2_000;

    /// <summary>Nombre total de chemins de jeu, plusieurs pouvant viser un même contenu.</summary>
    public int MaxGamePaths { get; init; } = 8_000;

    /// <summary>Longueur d'un chemin de jeu, en caractères.</summary>
    public int MaxGamePathLength { get; init; } = 256;

    /// <summary>Profondeur d'un chemin de jeu, en segments.</summary>
    public int MaxGamePathDepth { get; init; } = 16;
}
