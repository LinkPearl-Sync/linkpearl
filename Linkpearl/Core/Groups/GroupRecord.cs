using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>D'où vient un pair de groupe : quel groupe, et quelles empreintes.</summary>
/// <remarks>
/// Les deux empreintes servent à choisir l'initiateur du handshake : l'identifiant
/// du pair n'est pas connu d'avance, et les deux côtés doivent faire le même choix.
/// </remarks>
public sealed record GroupOrigin(GroupId Group, PlayerFingerprint Ours, PlayerFingerprint Theirs);

/// <summary>Un membre rencontré.</summary>
/// <remarks>
/// Indexé par empreinte et non par clé : c'est le personnage qu'on voit à
/// l'écran, et la clé ne s'apprend qu'au premier handshake, où elle s'épingle.
/// </remarks>
public sealed record GroupMember
{
    public required PlayerFingerprint Fingerprint { get; init; }

    /// <summary>La première clé vue pour ce personnage dans ce groupe. Null avant le premier handshake.</summary>
    public PeerId? Id { get; init; }

    /// <summary>Nom affiché, pour l'interface. Jamais transmis.</summary>
    public required string DisplayName { get; init; }

    public DateTimeOffset? LastSeenAt { get; init; }

    public bool Paused { get; init; }

    /// <summary>Les animations, VFX et sons acceptés de ce membre.</summary>
    public TransientCategories Receive { get; init; } = TransientCategories.All;
}

/// <summary>Un groupe dont le personnage est membre.</summary>
public sealed record GroupRecord
{
    public required GroupId Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Le secret partagé, 32 octets. Tout ce que le service voit en dérive.</summary>
    public required byte[] Secret { get; init; }

    public required IReadOnlyList<RendezvousAddress> Rendezvous { get; init; }

    public required DateTimeOffset JoinedAt { get; init; }

    /// <summary>Les membres rencontrés, et eux seuls : personne ne tient la liste complète.</summary>
    public IReadOnlyDictionary<PlayerFingerprint, GroupMember> Members { get; init; }
        = new Dictionary<PlayerFingerprint, GroupMember>();
}
