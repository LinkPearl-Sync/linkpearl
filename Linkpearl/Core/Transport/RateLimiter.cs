using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Transport;

/// <summary>Réglages du limiteur, dont le plafond est exposé à l'utilisateur.</summary>
public sealed record RateLimiterSettings
{
    public long InitialBytesPerSecond { get; init; } = 512 * 1024;

    /// <summary>Montée additive, appliquée une fois par intervalle de ligne saine.</summary>
    public long IncreaseBytesPerSecond { get; init; } = 128 * 1024;

    public TimeSpan IncreaseInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Chute multiplicative, appliquée dès le premier signe de congestion.</summary>
    public double DecreaseFactor { get; init; } = 0.7;

    public long CeilingBytesPerSecond { get; init; } = 8 * 1024 * 1024;

    public long FloorBytesPerSecond { get; init; } = 64 * 1024;

    /// <summary>Perte au-delà de laquelle on considère la ligne congestionnée.</summary>
    public double LossThresholdPercent { get; init; } = 1.0;

    /// <summary>
    /// Gonflement du temps d'aller-retour toléré avant de reculer.
    /// </summary>
    /// <remarks>
    /// C'est le signal le plus important : au jalon 2, la perte rapportée est
    /// restée nulle pendant que le ping doublait. Ne surveiller que la perte ne
    /// verrait donc rien du bufferbloat que l'on crée.
    /// </remarks>
    public double PingInflationThreshold { get; init; } = 1.30;

    /// <summary>Profondeur du seau, en secondes de débit.</summary>
    public double BurstSeconds { get; init; } = 0.25;
}

/// <summary>
/// Seau à jetons à adaptation additive-multiplicative.
/// </summary>
/// <remarks>
/// LiteNetLib n'a aucun contrôle de congestion, et sa file d'envoi n'est pas
/// bornée. Sans ce limiteur, on sature le lien montant, le bufferbloat monte, et
/// le ping de FFXIV part à trois cents millisecondes pendant qu'un pair
/// télécharge nos textures.
///
/// C'est donc autant une mesure sociale qu'une mesure technique : personne ne
/// veut voir son jeu ramer parce qu'une amie vient d'arriver à portée.
/// </remarks>
public sealed class RateLimiter(IClock clock, RateLimiterSettings settings)
{
    private double _tokens;
    private long _rate = settings.InitialBytesPerSecond;
    private DateTimeOffset _lastRefill = clock.UtcNow;
    private DateTimeOffset _lastRateChange = clock.UtcNow;
    private int _minimumPingMs = int.MaxValue;
    private bool _paused;

    public long BytesPerSecond => _rate;

    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;

            _paused = value;

            // À la reprise, le seau repart de zéro et l'horloge de remplissage
            // est recalée : sinon la fin d'un combat déclencherait une rafale de
            // plusieurs mégaoctets, exactement ce que la pause évitait.
            if (value is false)
            {
                _tokens = 0;
                _lastRefill = clock.UtcNow;
            }
        }
    }

    public bool TryConsume(int bytes)
    {
        if (_paused)
            return false;

        Refill();

        if (_tokens < bytes)
            return false;

        _tokens -= bytes;
        return true;
    }

    /// <summary>
    /// Alimente l'adaptation avec ce que le transport observe.
    /// </summary>
    public void Observe(double lossPercent, int pingMs)
    {
        if (pingMs > 0 && pingMs < _minimumPingMs)
            _minimumPingMs = pingMs;

        var congested = lossPercent > settings.LossThresholdPercent
                     || (_minimumPingMs < int.MaxValue
                         && pingMs > _minimumPingMs * settings.PingInflationThreshold);

        var now = clock.UtcNow;

        if (congested)
        {
            _rate = Math.Max(settings.FloorBytesPerSecond, (long)(_rate * settings.DecreaseFactor));
            _lastRateChange = now;
            return;
        }

        if (now - _lastRateChange < settings.IncreaseInterval)
            return;

        _rate = Math.Min(settings.CeilingBytesPerSecond, _rate + settings.IncreaseBytesPerSecond);
        _lastRateChange = now;
    }

    private void Refill()
    {
        var now = clock.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;

        if (elapsed <= 0)
            return;

        _lastRefill = now;
        _tokens = Math.Min(_rate * settings.BurstSeconds, _tokens + (elapsed * _rate));
    }
}
