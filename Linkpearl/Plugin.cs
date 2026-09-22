using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Linkpearl;

/// <summary>
/// Point d'entrée du plugin.
/// </summary>
/// <remarks>
/// Le plugin n'est qu'une coquille autour de <c>Core</c> : il fournit les
/// adaptateurs Dalamud et l'interface, et ne porte aucune logique. Tout ce qui
/// décide se teste sous Linux, sans le jeu.
/// </remarks>
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog              Log             { get; private set; } = null!;

    public Plugin()
    {
        Log.Information("Linkpearl chargé.");
    }

    public void Dispose()
    {
        // Le déchargement de Dalamud est coopératif : tout thread encore vivant
        // ici ferait fuir l'AssemblyLoadContext et le rechargement suivant en
        // créerait un second. Rien à libérer pour l'instant.
    }
}
