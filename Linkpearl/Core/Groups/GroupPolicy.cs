using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Comment un candidat entre dans un groupe.
/// </summary>
/// <remarks>
/// Des constantes et non une énumération : l'octet voyage dans la politique, et
/// tout ce qui vient du réseau se valide explicitement.
/// </remarks>
public static class AdmissionMode
{
    /// <summary>N'importe quel membre en ligne admet qui connaît le mot de passe.</summary>
    public const byte Password = 0x01;

    /// <summary>Seuls le propriétaire et les modérateurs admettent, un par un.</summary>
    public const byte Validation = 0x02;

    public static bool IsKnown(byte mode) => mode is Password or Validation;
}

/// <summary>Un bannissement : une clé, un personnage, ou les deux.</summary>
/// <remarks>
/// Les deux, d'ordinaire : bannir la seule clé laisserait revenir le même
/// personnage sous une identité neuve, bannir le seul personnage laisserait la
/// même personne revenir sous un autre.
/// </remarks>
public sealed record GroupBan(PeerId? Peer, PlayerFingerprint? Fingerprint)
{
    public bool Matches(PeerId? peer, PlayerFingerprint? fingerprint)
        => (Peer is { } banned && peer == banned) || (Fingerprint is { } character && fingerprint == character);
}

/// <summary>
/// Ce que seul le propriétaire décide, signé par la clé du groupe.
/// </summary>
/// <remarks>
/// Embarquée dans chaque politique pour qu'un membre qui vient d'entrer puisse
/// vérifier un modérateur sans rien connaître de l'historique : sans elle,
/// quiconque pourrait publier une politique où il se nomme lui-même.
/// </remarks>
/// <param name="Owner">La clé d'identité du propriétaire en tant que membre, compressée.</param>
/// <param name="Moderators">Les clés d'identité des modérateurs, compressées.</param>
public sealed record GroupAttestation(
    GroupId Group, ulong Version, byte Admission, byte[] Owner, IReadOnlyList<byte[]> Moderators, byte[] Signature);

/// <summary>Ce que le groupe dit de lui-même, signé par le groupe ou par un modérateur.</summary>
/// <param name="Code">Le ticket courant, 6 octets.</param>
/// <param name="Signer">La clé compressée du signataire : celle du groupe, ou celle d'un modérateur.</param>
public sealed record GroupPolicy(
    GroupId Group, ulong Version, string Name, byte[] Code, IReadOnlyList<RendezvousAddress> Rendezvous,
    string Password, IReadOnlyList<GroupBan> Bans, bool Dissolved, GroupAttestation Attestation,
    byte[] Signer, byte[] Signature)
{
    /// <summary>
    /// Un pair protégé n'est jamais tenu pour banni, même par une empreinte de
    /// personnage qui le viserait : sans cela, quiconque connaît le personnage
    /// d'un modérateur pourrait l'écarter par un simple bannissement par
    /// empreinte, que <see cref="GroupPolicyRules.TryAccept"/> n'a aucune raison
    /// de refuser puisqu'il ne cible aucune clé.
    /// </summary>
    public bool IsBanned(PeerId? peer, PlayerFingerprint? fingerprint)
    {
        if (peer is { } known && IsProtected(known))
            return false;

        return Bans.Any(ban => ban.Matches(peer, fingerprint));
    }

    /// <summary>Vrai si la clé appartient au propriétaire-membre ou à un modérateur attesté.</summary>
    public bool IsProtected(PeerId peer) => ProtectedPeers().Contains(peer);

    /// <summary>
    /// Les pairs qu'aucun bannissement par clé ne peut viser : le
    /// propriétaire-membre et chaque modérateur attesté.
    /// </summary>
    /// <remarks>
    /// Recalculé à chaque appel, jamais mis en cache : un <c>record</c>
    /// recopie ses champs privés dans un <c>with</c>, donc un cache survivrait
    /// à un remplacement de <see cref="Attestation"/> et garderait protégé un
    /// modérateur qu'une politique plus récente vient de retirer. Au plus dix-
    /// sept décompressions de point P-256, négligeable face au coût d'un
    /// bannissement mal jugé. Une clé qui ne se décompresse pas est ignorée
    /// sans lever : une politique seulement décodée, pas encore acceptée, peut
    /// en porter une mal formée, et <see cref="GroupPolicyRules.TryAccept"/> la
    /// rejette par ailleurs avant qu'on s'y fie.
    /// </remarks>
    public IReadOnlySet<PeerId> ProtectedPeers()
    {
        var protectedPeers = new HashSet<PeerId>();

        AddIfDecodable(protectedPeers, Attestation.Owner);

        foreach (var moderator in Attestation.Moderators)
            AddIfDecodable(protectedPeers, moderator);

        return protectedPeers;
    }

    private static void AddIfDecodable(HashSet<PeerId> peers, byte[] compressedKey)
    {
        try
        {
            peers.Add(PeerId.Of(CryptoPrimitives.Decompress(compressedKey)));
        }
        catch (CryptographicException)
        {
            // Voir la remarque de ProtectedPeers : ignorer plutôt que lever.
        }
    }

    public bool IsModerator(ReadOnlySpan<byte> compressedKey)
    {
        foreach (var moderator in Attestation.Moderators)
            if (moderator.AsSpan().SequenceEqual(compressedKey))
                return true;

        return false;
    }
}
