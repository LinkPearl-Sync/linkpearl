using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Ce qui se dérive du secret d'un groupe.
/// </summary>
/// <remarks>
/// Le service ne voit que des adresses de six octets et des jetons : rien ici ne
/// lui dit de quel groupe il s'agit, ni ne relie une fenêtre à la suivante.
/// </remarks>
public static class GroupDerivation
{
    public const int SecretSize = 32;

    private static ReadOnlySpan<byte> PresenceContext => "linkpearl:group-presence:v1"u8;
    private static ReadOnlySpan<byte> PairInfo => "linkpearl:group-pair:v1"u8;
    private static ReadOnlySpan<byte> RuntimeContext => "linkpearl:group-runtime:v1"u8;
    private static ReadOnlySpan<byte> AdmissionContext => "linkpearl:group-join:v1"u8;

    /// <summary>
    /// La boîte qu'un membre ouvre pour dire « je suis là » à son groupe.
    /// </summary>
    /// <remarks>
    /// Le secret y entre : sans lui, qui voit le personnage ne peut pas savoir
    /// s'il appartient au groupe. C'est ce qui distingue cette boîte de la boîte
    /// personnelle, calculable par n'importe qui.
    /// </remarks>
    public static MailboxAddress PresenceAddress(
        ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now, int windowOffset = 0)
    {
        if (secret.Length != SecretSize)
            throw new ArgumentException($"un secret de groupe fait {SecretSize} octets", nameof(secret));

        var index = MailboxAddress.IndexAt(now) + windowOffset;
        var memberAt = PresenceContext.Length + SecretSize;
        var windowAt = memberAt + PlayerFingerprint.SizeInBytes;

        Span<byte> input = stackalloc byte[windowAt + sizeof(long)];
        PresenceContext.CopyTo(input);
        secret.CopyTo(input[PresenceContext.Length..]);
        member.ToBytes().CopyTo(input[memberAt..]);
        BinaryPrimitives.WriteInt64BigEndian(input[windowAt..], index);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return MailboxAddress.FromBytes(digest[..MailboxAddress.SizeInBytes]);
    }

    /// <summary>La fenêtre courante et la suivante, pour la même raison que <see cref="MailboxAddress.Around"/>.</summary>
    public static IReadOnlyList<MailboxAddress> PresenceAround(
        ReadOnlySpan<byte> secret, PlayerFingerprint member, DateTimeOffset now)
        => [PresenceAddress(secret, member, now), PresenceAddress(secret, member, now, 1)];

    /// <summary>
    /// Le secret de paire de deux membres, calculé par chacun sans rien échanger.
    /// </summary>
    /// <remarks>
    /// Les empreintes entrent triées dans le sel, comme les identifiants dans
    /// <see cref="PairSecret.Derive"/> : les deux côtés doivent s'annoncer sous
    /// les mêmes jetons. Tout membre du groupe peut calculer celui de n'importe
    /// quel couple ; c'est le handshake, et l'épinglage de la clé, qui disent à
    /// qui l'on parle.
    /// </remarks>
    public static byte[] MemberPairSecret(ReadOnlySpan<byte> groupSecret, PlayerFingerprint one, PlayerFingerprint other)
    {
        if (one == other)
            throw new ArgumentException("un membre ne se compose pas avec lui-même", nameof(other));

        var a = one.ToBytes();
        var b = other.ToBytes();
        var (lower, upper) = a.AsSpan().SequenceCompareTo(b) < 0 ? (a, b) : (b, a);

        byte[] salt = [.. lower, .. upper];

        var prk = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, groupSecret, salt, prk);

        var secret = new byte[PairSecret.SizeInBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, secret, PairInfo);
        return secret;
    }

    /// <summary>
    /// L'identifiant sous lequel le moteur range un membre de groupe.
    /// </summary>
    /// <remarks>
    /// Le moteur indexe ses runtimes par <see cref="PeerId"/>, et un membre qu'on
    /// n'a jamais rencontré n'en a pas encore : sa clé n'arrive qu'au handshake.
    /// Celui-ci est stable pour un couple donné, et ne peut pas rencontrer un
    /// vrai <see cref="PeerId"/>, qui est le hash d'une clé publique.
    /// </remarks>
    public static PeerId RuntimeId(ReadOnlySpan<byte> pairSecret)
    {
        Span<byte> input = stackalloc byte[RuntimeContext.Length + pairSecret.Length];
        RuntimeContext.CopyTo(input);
        pairSecret.CopyTo(input[RuntimeContext.Length..]);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return PeerId.FromBytes(digest[..PeerId.SizeInBytes]);
    }

    /// <summary>
    /// La boîte où un candidat dépose sa demande, et que chaque membre ouvre.
    /// </summary>
    /// <remarks>
    /// Elle dérive du code et non du secret : le candidat ne connaît que le code.
    /// Changer le code dans la politique déplace la boîte, et l'ancien code ne
    /// mène plus nulle part.
    /// </remarks>
    public static MailboxAddress AdmissionAddress(ReadOnlySpan<byte> code, DateTimeOffset now, int windowOffset = 0)
    {
        if (code.Length != InvitationTicket.SizeInBytes)
            throw new ArgumentException($"un code fait {InvitationTicket.SizeInBytes} octets", nameof(code));

        var index = MailboxAddress.IndexAt(now) + windowOffset;
        var windowAt = AdmissionContext.Length + code.Length;

        Span<byte> input = stackalloc byte[windowAt + sizeof(long)];
        AdmissionContext.CopyTo(input);
        code.CopyTo(input[AdmissionContext.Length..]);
        BinaryPrimitives.WriteInt64BigEndian(input[windowAt..], index);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);

        return MailboxAddress.FromBytes(digest[..MailboxAddress.SizeInBytes]);
    }

    public static IReadOnlyList<MailboxAddress> AdmissionAround(ReadOnlySpan<byte> code, DateTimeOffset now)
        => [AdmissionAddress(code, now), AdmissionAddress(code, now, 1)];
}
