using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;

namespace Linkpearl.Core.Groups;

/// <summary>Ce que le handshake demande avant d'accepter la clé d'un pair de groupe.</summary>
public interface IGroupGate
{
    bool Admits(PairRecord pair, byte[] publicKey);
}

/// <summary>Le verdict sur la clé d'un membre. Strictement local.</summary>
public enum GroupAdmission
{
    Admitted,

    /// <summary>Admis, et épinglé à l'instant : il faut enregistrer.</summary>
    Pinned,

    /// <summary>Une autre clé a déjà été vue pour ce personnage.</summary>
    Disputed,

    UnknownGroup,
}

/// <summary>
/// Les groupes du personnage connecté.
/// </summary>
/// <remarks>
/// Lu par le fil de rafraîchissement, le tic du moteur et les handshakes, qui
/// tournent chacun sur sa tâche : il se protège seul. Les enregistrements sont
/// immuables, donc ce qui sort d'ici peut être lu sans verrou.
/// </remarks>
public sealed class GroupBook(IClock clock) : IGroupGate
{
    /// <summary>
    /// Chaque groupe coûte quatre boîtes par connexion au changement de fenêtre.
    /// Dix groupes tiennent sous la limite de 64 du service, avec la boîte personnelle.
    /// </summary>
    public const int MaxGroups = 10;

    /// <summary>Plus qu'une compagnie libre, assez peu pour que la liste reste lisible.</summary>
    public const int MaxMembersPerGroup = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<GroupId, GroupRecord> _groups = [];

    /// <summary>Levé hors du verrou, quand quelque chose qui s'enregistre a changé.</summary>
    public event Action? Changed;

    public IReadOnlyList<GroupRecord> All
    {
        get
        {
            lock (_gate)
                return [.. _groups.Values];
        }
    }

    public GroupRecord? Find(GroupId id)
    {
        lock (_gate)
            return _groups.GetValueOrDefault(id);
    }

    public bool TryAdd(GroupRecord group, out string? refusal)
    {
        lock (_gate)
        {
            if (_groups.ContainsKey(group.Id))
            {
                refusal = "déjà membre de ce groupe";
                return false;
            }

            if (_groups.Count >= MaxGroups)
            {
                refusal = $"au plus {MaxGroups} groupes par personnage";
                return false;
            }

            _groups[group.Id] = group;
        }

        refusal = null;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(GroupId id)
    {
        bool removed;

        lock (_gate)
            removed = _groups.Remove(id);

        if (removed)
            Changed?.Invoke();

        return removed;
    }

    /// <summary>Remplace tout, au chargement. Ne lève pas <see cref="Changed"/> : rien n'est à réécrire.</summary>
    public void Load(IEnumerable<GroupRecord> groups)
    {
        lock (_gate)
        {
            _groups.Clear();

            foreach (var group in groups.Take(MaxGroups))
                _groups[group.Id] = group;
        }
    }

    /// <summary>Oublie tout, au changement de personnage.</summary>
    public void Clear()
    {
        lock (_gate)
            _groups.Clear();
    }

    /// <summary>
    /// Décide si cette clé peut parler pour ce personnage dans ce groupe.
    /// </summary>
    /// <remarks>
    /// Tous les membres connaissent le secret, donc les jetons de n'importe quel
    /// couple : un membre pourrait se présenter à la place d'un autre. La
    /// première clé vue pour un personnage l'emporte, et toute autre est
    /// contestée. Le premier contact reste gagnable par qui arrive avant le
    /// vrai ; c'est un prix énoncé dans la spec.
    /// </remarks>
    public GroupAdmission Admit(GroupId id, PlayerFingerprint member, byte[] publicKey, string displayName)
    {
        var key = PeerId.Of(publicKey);
        GroupAdmission verdict;

        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false)
                return GroupAdmission.UnknownGroup;

            if (group.Members.TryGetValue(member, out var known) && known.Id is { } pinned)
            {
                // Une clé contestée ne prouve rien : n'importe quel membre connaît le
                // secret partagé et peut prétendre être n'importe quelle empreinte
                // avec une clé bidon, sans jamais réussir le handshake. La laisser
                // rafraîchir LastSeenAt fausserait l'ordre d'éviction de ForgetOldest
                // sans qu'aucune preuve n'ait été apportée.
                return pinned == key ? GroupAdmission.Admitted : GroupAdmission.Disputed;
            }

            var members = new Dictionary<PlayerFingerprint, GroupMember>(group.Members);

            if (known is not null)
            {
                verdict = GroupAdmission.Pinned;
                members[member] = known with { Id = key, LastSeenAt = clock.UtcNow };
            }
            else
            {
                verdict = GroupAdmission.Pinned;
                members[member] = new GroupMember
                {
                    Fingerprint = member,
                    Id = key,
                    DisplayName = displayName,
                    LastSeenAt = clock.UtcNow,
                };

                if (members.Count > MaxMembersPerGroup)
                    ForgetOldest(members, spare: member);
            }

            _groups[id] = group with { Members = members };
        }

        if (verdict is GroupAdmission.Pinned)
            Changed?.Invoke();

        return verdict;
    }

    public void SetPaused(GroupId id, PlayerFingerprint member, bool paused)
        => UpdateMember(id, member, known => known with { Paused = paused });

    public void SetReceive(GroupId id, PlayerFingerprint member, TransientCategories receive)
        => UpdateMember(id, member, known => known with { Receive = receive });

    bool IGroupGate.Admits(PairRecord pair, byte[] publicKey)
        => pair.Group is { } origin
           && Admit(origin.Group, origin.Theirs, publicKey, pair.DisplayName)
               is GroupAdmission.Admitted or GroupAdmission.Pinned;

    private void UpdateMember(GroupId id, PlayerFingerprint member, Func<GroupMember, GroupMember> change)
    {
        lock (_gate)
        {
            if (_groups.TryGetValue(id, out var group) is false
                || group.Members.TryGetValue(member, out var known) is false)
                return;

            var members = new Dictionary<PlayerFingerprint, GroupMember>(group.Members) { [member] = change(known) };
            _groups[id] = group with { Members = members };
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Oublie le membre vu il y a le plus longtemps.
    /// </summary>
    /// <remarks>
    /// Jamais un membre en pause : c'est un réglage que l'utilisateur a posé, et
    /// le retrouver effacé en silence serait pire que de garder un inconnu.
    /// </remarks>
    private static void ForgetOldest(Dictionary<PlayerFingerprint, GroupMember> members, PlayerFingerprint spare)
    {
        var oldest = members.Values
            .Where(candidate => candidate.Paused is false && candidate.Fingerprint != spare)
            .OrderBy(candidate => candidate.LastSeenAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

        if (oldest is not null)
            members.Remove(oldest.Fingerprint);
    }
}
