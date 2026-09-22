using Dalamud.Interface;

namespace Linkpearl.Ui.Shell;

/// <summary>
/// Entrée de navigation du shell.
/// </summary>
/// <remarks>
/// Le libellé et la pastille sont des fonctions et non des valeurs : ils sont
/// évalués à chaque image, pour que les compteurs se répercutent sans
/// reconstruire la navigation.
/// </remarks>
internal sealed class ShellPage
{
    public required string Id { get; init; }

    public required FontAwesomeIcon Icon { get; init; }

    /// <summary>Libellé affiché dans la barre latérale. Lu à chaque image.</summary>
    public required Func<string> Label { get; init; }

    /// <summary>Contenu de la page.</summary>
    public required Action Draw { get; init; }

    /// <summary>Compteur affiché en pastille. Zéro masque la pastille.</summary>
    public Func<int>? Badge { get; init; }

    /// <summary>Ancrée en bas de la barre latérale, séparée des autres.</summary>
    public bool Pinned { get; init; }
}
