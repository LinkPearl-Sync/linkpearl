using Dalamud.Plugin.Services;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Integration;

/// <summary>Le journal du noyau, versé dans celui de Dalamud.</summary>
/// <remarks>
/// Le noyau ne connaît pas Dalamud, et c'est ce qui le rend testable sous
/// Linux. Cette classe est le seul point où les deux se rencontrent.
///
/// Le préfixe nomme le sous-système et non le pair : aucun nom de personnage ne
/// passe par ici, le noyau n'en manipulant jamais.
/// </remarks>
public sealed class PluginLogSink(IPluginLog log, string subsystem) : ILogSink
{
    public void Debug(string message) => log.Debug($"[{subsystem}] {message}");

    public void Info(string message) => log.Information($"[{subsystem}] {message}");

    public void Warning(string message, Exception? exception = null)
    {
        if (exception is null)
            log.Warning($"[{subsystem}] {message}");
        else
            log.Warning(exception, $"[{subsystem}] {message}");
    }
}
