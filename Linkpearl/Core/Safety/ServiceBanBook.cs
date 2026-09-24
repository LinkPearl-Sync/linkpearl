using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Safety;

/// <summary>Qui a listé ce personnage, et pourquoi.</summary>
/// <remarks>Le motif vient du service : l'interface le normalise avant de l'afficher.</remarks>
public sealed record ServiceBan(RendezvousAddress Service, string Reason);

/// <summary>Le verdict des services sur un personnage. Strictement local.</summary>
public enum BanVerdict
{
    Clear,

    /// <summary>Une liste le concerne, et sa dérivation n'est pas encore faite.</summary>
    Pending,

    Listed,
}

public readonly record struct ServiceBanStatus(BanVerdict Verdict, ServiceBan? Ban)
{
    public static ServiceBanStatus Clear { get; } = new(BanVerdict.Clear, null);

    public static ServiceBanStatus Pending { get; } = new(BanVerdict.Pending, null);
}

/// <summary>Une dérivation à faire pour un personnage : le sel et le coût d'une liste.</summary>
public sealed record BanDerivation(string Key, byte[] Salt, BanParameters Parameters);

/// <summary>Ce que le planificateur, le moteur et l'interface demandent.</summary>
public interface IServiceBans
{
    ServiceBanStatus Status(PlayerFingerprint player);
}

/// <summary>
/// Les listes de bannissement des services actifs, appliquées par empreinte.
/// </summary>
/// <remarks>
/// Le noyau ne voit jamais le nom : c'est l'adaptateur qui dérive, à partir du
/// nom qu'il est seul à tenir, et qui rend le haché. On garde ce haché par
/// personnage et par sel, pas par liste : une liste qui s'allonge sous le même
/// sel ne coûte alors aucune dérivation de plus, et un nouveau banni se voit à
/// la seconde où la liste arrive.
///
/// Une liste vide ne demande rien : c'est le cas de presque tous les services,
/// et dériver pour elle coûterait des centaines de millisecondes par joueur
/// visible pour ne rien trouver.
///
/// Appelé depuis le fil de rafraîchissement, le tic du moteur, les remises de
/// boîte et l'interface : tout passe sous un verrou, et les dérivations, qui
/// sont longues, se font hors de lui.
/// </remarks>
public sealed class ServiceBanBook(IClock clock) : IServiceBans
{
    /// <summary>Au-delà, on oublie toutes les dérivations et on recommence.</summary>
    /// <remarks>
    /// Un lieu bondé compte une centaine de joueurs, fois deux ou trois sels :
    /// 4 096 laisse des heures de passage avant d'oublier. Tout oublier d'un
    /// coup est plus simple qu'un ordre d'ancienneté, et ne coûte que des
    /// dérivations refaites en arrière-plan.
    /// </remarks>
    public const int MaxRememberedDerivations = 4096;

    /// <summary>Dérivations à la demande permises par minute glissante.</summary>
    /// <remarks>
    /// Une demande de pairage ou d'admission vient d'un inconnu, qui peut en
    /// forger sous autant de noms qu'il veut : chacune coûterait une dérivation
    /// PBKDF2 complète. Vingt par minute couvrent largement les demandes
    /// honnêtes ; au-delà, le verdict reste en attente et la demande est ignorée.
    /// </remarks>
    public const int MaxOnDemandPerMinute = 20;

    private readonly Lock _gate = new();
    private readonly Dictionary<RendezvousAddress, BanList> _lists = [];
    private readonly Dictionary<(PlayerFingerprint Player, string Key), byte[]> _derived = [];
    private readonly Queue<DateTimeOffset> _onDemand = new();

    /// <summary>Levé hors du verrou quand une liste ou un verdict a pu changer.</summary>
    public event Action? Changed;

    public static string KeyOf(byte[] salt, BanParameters parameters)
        => $"{Convert.ToHexStringLower(salt)}:{parameters.Iterations}";

    public void SetList(RendezvousAddress service, BanList list)
    {
        lock (_gate)
            _lists[service] = list;

        Changed?.Invoke();
    }

    /// <summary>Oublie les listes des services qui ne sont plus actifs.</summary>
    public void Retain(IReadOnlyCollection<RendezvousAddress> services)
    {
        var removed = false;

        lock (_gate)
        {
            foreach (var service in _lists.Keys.Where(service => services.Contains(service) is false).ToList())
                removed |= _lists.Remove(service);
        }

        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Les dérivations qui manquent pour trancher sur ce personnage.</summary>
    public IReadOnlyList<BanDerivation> Missing(PlayerFingerprint player)
    {
        lock (_gate)
        {
            return [.. _lists.Values
                .Where(list => list.Entries.Count > 0)
                .Select(list => new BanDerivation(KeyOf(list.Salt, list.Parameters), list.Salt, list.Parameters))
                .DistinctBy(derivation => derivation.Key)
                .Where(derivation => _derived.ContainsKey((player, derivation.Key)) is false)];
        }
    }

    public void Record(PlayerFingerprint player, string key, byte[] derived)
    {
        lock (_gate)
        {
            if (_derived.Count >= MaxRememberedDerivations)
                _derived.Clear();

            _derived[(player, key)] = derived;
        }

        Changed?.Invoke();
    }

    public ServiceBanStatus Status(PlayerFingerprint player)
    {
        lock (_gate)
        {
            var pending = false;

            // Dans un ordre fixe : deux services qui listent le même joueur
            // doivent donner le même motif d'une image à l'autre.
            foreach (var (service, list) in _lists.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
            {
                if (list.Entries.Count == 0)
                    continue;

                if (_derived.TryGetValue((player, KeyOf(list.Salt, list.Parameters)), out var hash) is false)
                {
                    pending = true;
                    continue;
                }

                foreach (var entry in list.Entries)
                {
                    // À temps constant, comme BanList.Contains.
                    if (CryptographicOperations.FixedTimeEquals(entry.Hash, hash))
                        return new ServiceBanStatus(BanVerdict.Listed, new ServiceBan(service, entry.Reason));
                }
            }

            return pending ? ServiceBanStatus.Pending : ServiceBanStatus.Clear;
        }
    }

    /// <summary>
    /// Tranche tout de suite, en dérivant ce qui manque, dans la limite du budget.
    /// </summary>
    /// <remarks>
    /// Pour une demande venue du réseau, qu'on ne peut pas laisser en attente
    /// d'une ronde de détection. La dérivation se fait sur le fil de l'appelant
    /// et hors du verrou : c'est lui qui a choisi d'attendre.
    /// </remarks>
    public ServiceBanStatus Screen(PlayerFingerprint player, Func<byte[], BanParameters, byte[]> derive)
    {
        foreach (var missing in Missing(player))
        {
            lock (_gate)
            {
                var now = clock.UtcNow;

                while (_onDemand.Count > 0 && now - _onDemand.Peek() >= TimeSpan.FromMinutes(1))
                    _onDemand.Dequeue();

                if (_onDemand.Count >= MaxOnDemandPerMinute)
                    return ServiceBanStatus.Pending;

                _onDemand.Enqueue(now);
            }

            Record(player, missing.Key, derive(missing.Salt, missing.Parameters));
        }

        return Status(player);
    }
}
