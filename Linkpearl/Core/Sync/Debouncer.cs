using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Sync;

/// <summary>
/// Regroupe une rafale de changements en un seul recalcul.
/// </summary>
/// <remarks>
/// Un changement de tenue produit une dizaine d'événements en quelques
/// centaines de millisecondes. Recalculer le manifeste à chaque fois hacherait
/// des centaines de mégaoctets pour rien, et annoncerait au pair une dizaine
/// d'apparences intermédiaires dont aucune n'est celle qu'on voulait montrer.
///
/// Le plafond existe pour que des changements continus finissent quand même par
/// être annoncés : sans lui, quelqu'un qui bricole son apparence pendant dix
/// minutes ne serait jamais synchronisé.
/// </remarks>
public sealed class Debouncer(IClock clock, TimeSpan quietPeriod, TimeSpan cap)
{
    private DateTimeOffset? _firstSignal;
    private DateTimeOffset _lastSignal;

    public bool IsPending => _firstSignal is not null;

    public void Signal()
    {
        _firstSignal ??= clock.UtcNow;
        _lastSignal = clock.UtcNow;
    }

    /// <summary>Vrai une seule fois, quand le calme est revenu ou le plafond atteint.</summary>
    public bool TryConsume()
    {
        if (_firstSignal is not { } first)
            return false;

        var now = clock.UtcNow;

        if (now - _lastSignal < quietPeriod && now - first < cap)
            return false;

        _firstSignal = null;
        return true;
    }

    public void Cancel() => _firstSignal = null;
}
