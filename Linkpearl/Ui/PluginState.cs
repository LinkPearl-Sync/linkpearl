using Linkpearl.Integration;

namespace Linkpearl.Ui;

/// <summary>
/// L'état que l'interface lit.
/// </summary>
/// <remarks>
/// Préparé par une tâche de fond, jamais calculé pendant le dessin : une image
/// bloquée, c'est le jeu qui saccade.
/// </remarks>
public sealed class PluginState
{
    public IReadOnlyList<NearbyPlayer> Nearby { get; set; } = [];

    public NearbyPlayer? Self { get; set; }
}
