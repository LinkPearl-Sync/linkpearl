using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Sync;

/// <summary>Un service de rendez-vous de la liste de l'utilisateur.</summary>
/// <remarks>
/// L'interrupteur permet d'en écarter un sans perdre son adresse : un service
/// en panne se désactive le temps qu'il revienne, plutôt que de se retaper.
/// </remarks>
public sealed record RendezvousEntry(RendezvousAddress Address, string Label, bool Enabled);

/// <summary>
/// La liste des services de l'utilisateur, et sa migration.
/// </summary>
/// <remarks>
/// La migration vit ici et non dans <c>Configuration</c>, qui dépend de Dalamud
/// et ne se testerait pas sous Linux. Le plugin n'appelle qu'une fonction.
/// </remarks>
public static class RendezvousList
{
    /// <summary>
    /// Construit la liste depuis l'ancien réglage, si elle n'existe pas encore.
    /// </summary>
    /// <remarks>
    /// Rend la liste courante telle quelle dès qu'elle porte quelque chose : la
    /// migration ne doit jamais écraser un choix que l'utilisateur a fait depuis.
    /// </remarks>
    public static IReadOnlyList<RendezvousEntry> Migrate(
        string? legacyHost, int legacyPort, IReadOnlyList<RendezvousEntry>? current)
    {
        if (current is { Count: > 0 })
            return current;

        if (string.IsNullOrWhiteSpace(legacyHost))
            return [];

        var text = legacyPort == RendezvousAddress.DefaultPort ? legacyHost : $"{legacyHost}:{legacyPort}";

        return RendezvousAddress.TryParse(text, out var address, out _)
            ? [new RendezvousEntry(address, "", true)]
            : [];
    }
}
