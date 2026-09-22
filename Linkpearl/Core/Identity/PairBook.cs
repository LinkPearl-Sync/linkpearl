using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Identity;

/// <summary>Degré de confiance accordé à un pair.</summary>
/// <remarks>
/// Une énumération est acceptable ici parce que cet état ne traverse jamais le
/// réseau : il est strictement local. Ce qui vient du réseau se valide octet par
/// octet, et n'est jamais converti en énumération sans contrôle.
/// </remarks>
public enum PairTrust
{
    Pending,
    Accepted,
    Blocked,
}

/// <summary>Ce qu'on accepte d'échanger avec un pair, dans chaque sens.</summary>
[Flags]
public enum PairPermissions
{
    None = 0,
    ReceiveAppearance = 1,
    SendAppearance = 2,
    Both = ReceiveAppearance | SendAppearance,
}

/// <summary>Comment on accepte de se connecter à un pair.</summary>
public enum ConnectionPolicy
{
    /// <summary>Direct si possible, relais en secours.</summary>
    Direct,

    /// <summary>
    /// Relais uniquement.
    /// </summary>
    /// <remarks>
    /// Une connexion directe révèle notre adresse IP au pair, donc notre ville
    /// et notre opérateur. Ce mode existe pour les pairs à qui l'on ne fait pas
    /// cette confiance-là, et c'est une propriété que Mare n'offrait pas.
    /// </remarks>
    RelayOnly,
}

/// <summary>Un pair du carnet.</summary>
public sealed record PairRecord
{
    public required PeerId Id { get; init; }
    public required byte[] PublicKey { get; init; }
    public required byte[] PairSecret { get; init; }

    /// <summary>Nom donné localement. Jamais transmis.</summary>
    public required string DisplayName { get; init; }

    public required string RendezvousHost { get; init; }

    public PairTrust Trust { get; init; } = PairTrust.Pending;
    public PairPermissions Permissions { get; init; } = PairPermissions.Both;
    public ConnectionPolicy Policy { get; init; } = ConnectionPolicy.Direct;
    public bool Paused { get; init; }

    public required DateTimeOffset PairedAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>Empreinte du personnage du pair, épinglée à la première rencontre.</summary>
    /// <remarks>
    /// Sans épinglage, un pair pourrait annoncer l'empreinte d'un tiers et nous
    /// faire appliquer ses fichiers sur ce tiers, visible chez nous seuls.
    /// </remarks>
    public PlayerFingerprint? PinnedFingerprint { get; init; }
}

/// <summary>
/// Le carnet de pairs. C'est lui, et lui seul, qui autorise.
/// </summary>
/// <remarks>
/// Le service de rendez-vous n'a aucun rôle dans cette décision. Il peut
/// refuser son service ou mentir sur une adresse, ce qui produit un échec de
/// connexion ; il ne peut pas faire accepter un pair.
/// </remarks>
public sealed class PairBook(IClock clock)
{
    private readonly Dictionary<PeerId, PairRecord> _pairs = [];

    public IReadOnlyCollection<PairRecord> All => _pairs.Values;

    public IEnumerable<PairRecord> Active =>
        _pairs.Values.Where(p => p.Trust is PairTrust.Accepted && p.Paused is false);

    public PairRecord? Find(PeerId id) => _pairs.GetValueOrDefault(id);

    public PairRecord? Find(ReadOnlySpan<byte> publicKey) => Find(PeerId.Of(publicKey));

    /// <summary>
    /// Ajoute un pair depuis un code d'invitation, en attente de confirmation.
    /// </summary>
    public PairRecord Invite(PairingCode code, string displayName, ReadOnlySpan<byte> ourPublicKey)
    {
        var record = new PairRecord
        {
            Id = code.Id,
            PublicKey = code.PublicKey,
            PairSecret = PairSecret.Derive(code.PairingNonce, ourPublicKey.ToArray(), code.PublicKey),
            DisplayName = displayName,
            RendezvousHost = code.RendezvousHost,
            Trust = PairTrust.Pending,
            PairedAt = clock.UtcNow,
        };

        _pairs[record.Id] = record;
        return record;
    }

    public void Accept(PeerId id) => Update(id, record => record with { Trust = PairTrust.Accepted });

    public void Block(PeerId id) => Update(id, record => record with { Trust = PairTrust.Blocked });

    public void SetPaused(PeerId id, bool paused) => Update(id, record => record with { Paused = paused });

    public void SetPolicy(PeerId id, ConnectionPolicy policy) => Update(id, record => record with { Policy = policy });

    public void SetPermissions(PeerId id, PairPermissions permissions)
        => Update(id, record => record with { Permissions = permissions });

    public void PinFingerprint(PeerId id, PlayerFingerprint fingerprint)
        => Update(id, record => record with { PinnedFingerprint = fingerprint });

    public void Seen(PeerId id) => Update(id, record => record with { LastSeenAt = clock.UtcNow });

    public bool Remove(PeerId id) => _pairs.Remove(id);

    /// <summary>
    /// Décide si une clé publique reçue dans un handshake est acceptable.
    /// </summary>
    /// <remarks>
    /// Un pair en attente est accepté au niveau cryptographique : c'est ce qui
    /// permet à la première connexion d'aboutir et à l'utilisateur de voir une
    /// demande à confirmer. Rien ne lui sera appliqué tant qu'il n'est pas
    /// accepté pour de bon.
    /// </remarks>
    public bool IsAuthorized(byte[] publicKey)
        => Find(publicKey) is { Trust: PairTrust.Pending or PairTrust.Accepted };

    public void Load(IEnumerable<PairRecord> records)
    {
        _pairs.Clear();

        foreach (var record in records)
            _pairs[record.Id] = record;
    }

    private void Update(PeerId id, Func<PairRecord, PairRecord> change)
    {
        if (_pairs.TryGetValue(id, out var record))
            _pairs[id] = change(record);
    }
}
