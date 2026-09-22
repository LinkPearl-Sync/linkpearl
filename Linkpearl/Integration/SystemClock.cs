using Linkpearl.Core.Abstractions;

namespace Linkpearl.Integration;

/// <summary>
/// L'horloge système.
/// </summary>
/// <remarks>
/// Elle vit ici et non dans Core/, où un test d'architecture interdit la lecture
/// directe de l'heure. C'est la seule implémentation qui a le droit d'appeler
/// DateTimeOffset.UtcNow.
/// </remarks>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
