using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;

namespace Linkpearl.Core.Groups;

/// <summary>
/// Les types de dépôt de l'admission, à la suite de ceux du pairage (0x03, 0x04).
/// </summary>
public static class AdmissionKind
{
    public const byte Request = 0x05;
    public const byte Challenge = 0x06;
    public const byte Proof = 0x07;
    public const byte Welcome = 0x08;
    public const byte Refusal = 0x09;

    public static bool IsAdmission(byte kind) => kind is >= Request and <= Refusal;
}

/// <summary>Pourquoi un membre refuse un candidat.</summary>
public static class RefusalReason
{
    public const byte WrongPassword = 0x01;
    public const byte TooManyAttempts = 0x02;
    public const byte Declined = 0x03;

    public static bool IsKnown(byte reason) => reason is WrongPassword or TooManyAttempts or Declined;
}

public abstract record AdmissionMessage;

/// <summary>Candidat vers la boîte d'admission : « je veux entrer ».</summary>
/// <param name="PublicKey">Clé d'identité du candidat, point de 65 octets.</param>
/// <param name="Ephemeral">Éphémère du candidat, point de 65 octets.</param>
public sealed record AdmissionRequest(
    byte[] Code, byte[] PublicKey, byte[] Ephemeral, byte[] Nonce, ushort WorldId, string CharacterName) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : « prouve que tu connais le mot de passe ».</summary>
public sealed record AdmissionChallenge(byte[] Nonce, byte[] MemberEphemeral) : AdmissionMessage;

/// <summary>Candidat vers la boîte d'admission : le mot de passe, scellé pour le membre qui a défié.</summary>
public sealed record AdmissionProof(byte[] Code, byte[] Nonce, byte[] MemberEphemeral, byte[] SealedPassword) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : l'octroi du groupe, scellé.</summary>
public sealed record AdmissionWelcome(byte[] Nonce, byte[] MemberEphemeral, byte[] SealedGrant) : AdmissionMessage;

/// <summary>Membre vers la boîte du candidat : non, avec le motif.</summary>
/// <remarks>Non authentifié : un tiers peut le forger, ce qui ne coûte qu'un nouvel essai au candidat.</remarks>
public sealed record AdmissionRefusal(byte[] Nonce, byte Reason) : AdmissionMessage;

/// <summary>
/// Les dépôts de l'admission, chacun sous les 512 octets d'une boîte.
/// </summary>
/// <remarks>
/// Disposition, après l'octet de type :
/// <code>
/// demande   : code (6) | clé (33) | éphémère (33) | aléa (12) | monde (2) | nom (reste, 1 à 64)
/// défi      : aléa (12) | éphémère du membre (33)
/// preuve    : code (6) | aléa (12) | éphémère du membre (33) | mot de passe scellé (reste)
/// bienvenue : aléa (12) | éphémère du membre (33) | octroi scellé (reste)
/// refus     : aléa (12) | motif (1)
/// </code>
/// Points compressés sur le fil, non compressés en mémoire, comme la demande de pairage.
/// </remarks>
public static class AdmissionCodec
{
    public const int NonceLength = 12;
    public const int MaxNameBytes = 64;

    private const int CodeLength = InvitationTicket.SizeInBytes;
    private const int PointLength = CryptoPrimitives.CompressedPointLength;
    private const int TagLength = CryptoPrimitives.TagLength;

    public static byte[] Encode(AdmissionMessage message) => message switch
    {
        AdmissionRequest request => EncodeRequest(request),
        AdmissionChallenge challenge =>
            [AdmissionKind.Challenge, .. challenge.Nonce, .. CryptoPrimitives.Compress(challenge.MemberEphemeral)],
        AdmissionProof proof =>
            [AdmissionKind.Proof, .. proof.Code, .. proof.Nonce, .. CryptoPrimitives.Compress(proof.MemberEphemeral), .. proof.SealedPassword],
        AdmissionWelcome welcome =>
            [AdmissionKind.Welcome, .. welcome.Nonce, .. CryptoPrimitives.Compress(welcome.MemberEphemeral), .. welcome.SealedGrant],
        AdmissionRefusal refusal => [AdmissionKind.Refusal, .. refusal.Nonce, refusal.Reason],
        _ => throw new ArgumentException("dépôt d'admission inconnu", nameof(message)),
    };

    private static byte[] EncodeRequest(AdmissionRequest request)
    {
        var name = Encoding.UTF8.GetBytes(request.CharacterName);

        if (name.Length > MaxNameBytes)
            throw new ArgumentException("nom de personnage trop long", nameof(request));

        var world = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(world, request.WorldId);

        return [
            AdmissionKind.Request, .. request.Code, .. CryptoPrimitives.Compress(request.PublicKey),
            .. CryptoPrimitives.Compress(request.Ephemeral), .. request.Nonce, .. world, .. name,
        ];
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out AdmissionMessage? message, out string? rejection)
    {
        message = null;

        if (payload.IsEmpty || AdmissionKind.IsAdmission(payload[0]) is false)
            return Refuse("dépôt qui n'est pas une admission", out rejection);

        var body = payload[1..];

        try
        {
            switch (payload[0])
            {
                case AdmissionKind.Request:
                {
                    const int header = CodeLength + PointLength + PointLength + NonceLength + 2;

                    if (body.Length <= header || body.Length > header + MaxNameBytes)
                        return Refuse("demande d'admission hors bornes", out rejection);

                    var name = Encoding.UTF8.GetString(body[header..]);

                    if (name.Any(char.IsControl) || string.IsNullOrWhiteSpace(name))
                        return Refuse("nom de personnage hors règles", out rejection);

                    message = new AdmissionRequest(
                        body[..CodeLength].ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength, PointLength)),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength + PointLength, PointLength)),
                        body.Slice(CodeLength + (2 * PointLength), NonceLength).ToArray(),
                        BinaryPrimitives.ReadUInt16BigEndian(body.Slice(header - 2, 2)),
                        name);
                    break;
                }

                case AdmissionKind.Challenge:
                    if (body.Length != NonceLength + PointLength)
                        return Refuse("défi hors bornes", out rejection);

                    message = new AdmissionChallenge(body[..NonceLength].ToArray(), CryptoPrimitives.Decompress(body[NonceLength..]));
                    break;

                case AdmissionKind.Proof:
                {
                    const int header = CodeLength + NonceLength + PointLength;
                    var sealedLength = body.Length - header;

                    if (sealedLength < TagLength || sealedLength > TagLength + GroupPolicyCodec.MaxPasswordBytes)
                        return Refuse("preuve hors bornes", out rejection);

                    message = new AdmissionProof(
                        body[..CodeLength].ToArray(),
                        body.Slice(CodeLength, NonceLength).ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(CodeLength + NonceLength, PointLength)),
                        body[header..].ToArray());
                    break;
                }

                case AdmissionKind.Welcome:
                {
                    const int header = NonceLength + PointLength;
                    var sealedLength = body.Length - header;

                    if (sealedLength < GroupGrantCodec.MinSealedLength || sealedLength > GroupGrantCodec.MaxSealedLength)
                        return Refuse("bienvenue hors bornes", out rejection);

                    message = new AdmissionWelcome(
                        body[..NonceLength].ToArray(),
                        CryptoPrimitives.Decompress(body.Slice(NonceLength, PointLength)),
                        body[header..].ToArray());
                    break;
                }

                case AdmissionKind.Refusal:
                    if (body.Length != NonceLength + 1 || RefusalReason.IsKnown(body[NonceLength]) is false)
                        return Refuse("refus hors bornes", out rejection);

                    message = new AdmissionRefusal(body[..NonceLength].ToArray(), body[NonceLength]);
                    break;
            }
        }
        catch (CryptographicException e)
        {
            return Refuse($"clé invalide : {e.Message}", out rejection);
        }

        rejection = null;
        return message is not null;
    }

    private static bool Refuse(string why, out string? rejection)
    {
        rejection = why;
        return false;
    }
}

/// <summary>Ce qu'un membre remet au candidat admis : de quoi être du groupe.</summary>
/// <param name="OwnerKey">La clé compressée du groupe, dont dérive son identifiant.</param>
public sealed record GroupGrant(GroupId Group, byte[] Secret, byte[] OwnerKey, string Name);

public static class GroupGrantCodec
{
    private const int FixedLength = GroupId.SizeInBytes + GroupDerivation.SecretSize + CryptoPrimitives.CompressedPointLength + 1;

    public const int MinSealedLength = FixedLength + 1 + CryptoPrimitives.TagLength;
    public const int MaxSealedLength = FixedLength + GroupPolicyCodec.MaxNameBytes + CryptoPrimitives.TagLength;

    public static byte[] Encode(GroupGrant grant)
    {
        var name = Encoding.UTF8.GetBytes(grant.Name);
        return [.. grant.Group.ToBytes(), .. grant.Secret, .. grant.OwnerKey, checked((byte)name.Length), .. name];
    }

    public static bool TryDecode(ReadOnlySpan<byte> plain, out GroupGrant? grant, out string? rejection)
    {
        grant = null;

        if (plain.Length < FixedLength || plain.Length != FixedLength + plain[FixedLength - 1])
        {
            rejection = "octroi mal formé";
            return false;
        }

        var group = GroupId.FromBytes(plain[..GroupId.SizeInBytes]);
        var secret = plain.Slice(GroupId.SizeInBytes, GroupDerivation.SecretSize).ToArray();
        var ownerKey = plain.Slice(GroupId.SizeInBytes + GroupDerivation.SecretSize, CryptoPrimitives.CompressedPointLength).ToArray();
        string name;

        try
        {
            name = new UTF8Encoding(false, true).GetString(plain[FixedLength..]);
        }
        catch (DecoderFallbackException)
        {
            rejection = "nom de groupe illisible";
            return false;
        }

        // L'identifiant doit dériver de la clé : sinon un membre malveillant
        // ferait croire au candidat qu'il entre dans un groupe alors qu'il entre
        // dans un autre.
        if (GroupId.Of(ownerKey) != group || GroupPolicyCodec.IsValidName(name) is false)
        {
            rejection = "octroi incohérent";
            return false;
        }

        grant = new GroupGrant(group, secret, ownerKey, name);
        rejection = null;
        return true;
    }
}

/// <summary>
/// Le scellement de l'admission : la preuve et l'octroi.
/// </summary>
/// <remarks>
/// La clé vient de l'accord des éphémères suivi de l'aléa, comme le pairage,
/// dérivée par usage : une preuve ne s'ouvre jamais comme une bienvenue. Chaque
/// clé ne sert qu'une fois, d'où l'aléa AES-GCM fixe à zéro. Les données
/// associées lient le scellé à son en-tête.
/// </remarks>
public static class AdmissionSealing
{
    public const string Proof = "proof";
    public const string Welcome = "welcome";

    private static readonly byte[] ZeroNonce = new byte[CryptoPrimitives.NonceLength];

    public static byte[] DeriveKey(ReadOnlySpan<byte> shared, ReadOnlySpan<byte> nonce, string purpose)
    {
        byte[] material = [.. shared, .. nonce];

        try
        {
            var key = new byte[CryptoPrimitives.KeyLength];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, material, key, [], Encoding.ASCII.GetBytes("linkpearl:group-admit:v1:" + purpose));
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    public static byte[] Key(ECDiffieHellman ours, ReadOnlySpan<byte> theirEphemeral, ReadOnlySpan<byte> nonce, string purpose)
    {
        var shared = CryptoPrimitives.Agree(ours, theirEphemeral);

        try
        {
            return DeriveKey(shared, nonce, purpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    public static byte[] Associated(byte kind, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> memberEphemeral)
        => [kind, .. nonce, .. CryptoPrimitives.Compress(memberEphemeral)];

    public static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associated)
        => CryptoPrimitives.Seal(key, ZeroNonce, plaintext, associated);

    public static bool TryOpen(byte[] key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associated, out byte[] plaintext)
        => CryptoPrimitives.TryOpen(key, ZeroNonce, sealedData, associated, out plaintext);
}
