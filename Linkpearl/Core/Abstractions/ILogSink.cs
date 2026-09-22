namespace Linkpearl.Core.Abstractions;

/// <summary>
/// Le journal, injecté.
/// </summary>
/// <remarks>
/// Le noyau ne connaît pas Dalamud, donc pas son journal. Et un noyau qui
/// écrirait directement sur la console serait intestable.
/// </remarks>
public interface ILogSink
{
    void Debug(string message);

    void Info(string message);

    void Warning(string message, Exception? exception = null);
}
