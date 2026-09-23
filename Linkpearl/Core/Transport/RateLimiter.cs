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

    /// <summary>
    /// Gonflement absolu en deçà duquel un ping n'est jamais une congestion.
    /// </summary>
    /// <remarks>
    /// Le seuil relatif seul ne tient pas sur un lien local : vu en jeu entre
    /// deux clients du même poste, un aller-retour de 2 ms qui passait à 4
    /// suffisait à faire tomber le débit au plancher en dix secondes. Le
    /// bufferbloat qui gêne le jeu se compte en dizaines de millisecondes : au
    /// jalon 2, 60 ms devenaient 145.
    /// </remarks>
    public int PingInflationMarginMs { get; init; } = 20;

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
    private readonly Lock _gate = new();

    private double _tokens;
    private long _rate = settings.InitialBytesPerSecond;
    private DateTimeOffset _lastRefill = clock.UtcNow;
    private DateTimeOffset _lastRateChange = clock.UtcNow;
    private int _minimumPingMs = int.MaxValue;
    private bool _paused;

    public long BytesPerSecond => _rate;

    /// <summary>
    /// Débrayé : tout passe, sauf pendant une pause.
    /// </summary>
    /// <remarks>
    /// Au choix de l'utilisateur. Des rôlistes posés dans une taverne n'ont que
    /// faire de quelques millisecondes de ping, et préfèrent voir une tenue
    /// arriver en secondes plutôt qu'en minutes. La contre-pression par canal
    /// reste, elle, et c'est elle qui borne la mémoire.
    /// </remarks>
    public bool Bypassed { get; set; }

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

    /// <summary>
    /// Prend des jetons pour un bloc, ou rend faux.
    /// </summary>
    /// <remarks>
    /// Sous verrou : plusieurs blobs sont servis de front, et sans lui le seau
    /// se viderait deux fois pour un seul bloc, ou pas du tout.
    /// </remarks>
    public bool TryConsume(int bytes)
    {
        lock (_gate)
        {
            if (_paused)
                return false;

            if (Bypassed)
                return true;

            Refill();

            if (_tokens < bytes)
                return false;

            _tokens -= bytes;
            return true;
        }
    }

    /// <summary>
    /// Alimente l'adaptation avec ce que le transport observe.
    /// </summary>
    public void Observe(double lossPercent, int pingMs)
    {
        lock (_gate)
            Adapt(lossPercent, pingMs);
    }

    private void Adapt(double lossPercent, int pingMs)
    {
        if (pingMs > 0 && pingMs < _minimumPingMs)
            _minimumPingMs = pingMs;

        var congested = lossPercent > settings.LossThresholdPercent
                     || (_minimumPingMs < int.MaxValue
                         && pingMs > _minimumPingMs * settings.PingInflationThreshold
                         && pingMs > _minimumPingMs + settings.PingInflationMarginMs);

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
