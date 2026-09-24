using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;

namespace Linkpearl.Core.Groups;

/// <summary>
/// L'encodage canonique d'une politique, celui qui est signé.
/// </summary>
/// <remarks>
/// Binaire et non JSON : deux membres doivent produire les mêmes octets pour
/// la même politique, sans quoi aucune signature ne se vérifierait. Entiers en
/// gros-boutiste, textes précédés d'un octet de longueur.
///
/// Disposition :
/// <code>
/// attestation : format (1) | groupe (16) | version (8) | admission (1) | propriétaire (33)
///               | n (1) | n × modérateur (33) | signature (64)
/// politique   : format (1) | groupe (16) | version (8) | nom (1+) | code (6)
///               | n (1) | n × service (1+) | mot de passe (1+) | n (2) | n × banni
///               | dissous (1) | taille (2) | attestation | signataire (33) | signature (64)
/// banni       : drapeaux (1, 1 = clé, 2 = personnage) | clé (16)? | personnage (16)?
/// </code>
/// </remarks>
public static class GroupPolicyCodec
{
    public const byte Format = 0x01;
    public const int MaxNameChars = 32;
    public const int MaxNameBytes = 128;
    public const int MaxRendezvous = 4;
    public const int MaxRendezvousLength = 255;
    public const int MaxPasswordBytes = 64;
    public const int MaxBans = 256;
    public const int MaxModerators = 16;

    /// <summary>Largement au-dessus du pire cas (environ 11 Kio), assez bas pour qu'un pair ne nous fasse rien allouer d'absurde.</summary>
    public const int MaxEncodedLength = 16 * 1024;

    private const byte BanPeer = 0x01;
    private const byte BanFingerprint = 0x02;
    private const int KeyLength = CryptoPrimitives.CompressedPointLength;
    private const int SignatureLength = CryptoPrimitives.SignatureLength;
    private const int CodeLength = InvitationTicket.SizeInBytes;

    private static ReadOnlySpan<byte> AttestationLabel => "linkpearl:group-attest:v1"u8;
    private static ReadOnlySpan<byte> PolicyLabel => "linkpearl:group-policy:v1"u8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Un nom de groupe : 1 à 32 caractères, sans caractère de contrôle, pas seulement des espaces.</summary>
    public static bool IsValidName(string? name)
        => name is { Length: > 0 and <= MaxNameChars }
           && string.IsNullOrWhiteSpace(name) is false
           && name.Any(char.IsControl) is false
           && Encoding.UTF8.GetByteCount(name) <= MaxNameBytes;

    public static byte[] Encode(GroupPolicy policy)
    {
        var writer = new ArrayBufferWriter<byte>();
        WritePolicyBody(writer, policy);
        writer.Write(policy.Signature);
        return writer.WrittenSpan.ToArray();
    }

    public static byte[] EncodeAttestation(GroupAttestation attestation)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteAttestationBody(writer, attestation);
        writer.Write(attestation.Signature);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Ce que signe le signataire de la politique : l'étiquette, puis tout sauf sa signature.</summary>
    public static byte[] SignedPortion(GroupPolicy policy)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(PolicyLabel);
        WritePolicyBody(writer, policy);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Ce que signe la clé du groupe dans l'attestation.</summary>
    public static byte[] AttestationSignedPortion(GroupAttestation attestation)
    {
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(AttestationLabel);
        WriteAttestationBody(writer, attestation);
        return writer.WrittenSpan.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out GroupPolicy? policy, out string? rejection)
    {
        policy = null;

        if (encoded.Length > MaxEncodedLength)
            return Refuse("politique démesurée", out rejection);

        var reader = new Reader(encoded);

        if (reader.TryByte(out var format) is false || format != Format)
            return Refuse("format de politique inconnu", out rejection);

        if (reader.TryTake(GroupId.SizeInBytes, out var group) is false || reader.TryU64(out var version) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.TryText(MaxNameBytes, out var name) is false || IsValidName(name) is false)
            return Refuse("nom de groupe hors règles", out rejection);

        if (reader.TryTake(CodeLength, out var code) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.TryByte(out var placeCount) is false || placeCount is 0 or > MaxRendezvous)
            return Refuse("nombre de services hors bornes", out rejection);

        var places = new List<RendezvousAddress>(placeCount);

        for (var i = 0; i < placeCount; i++)
        {
            if (reader.TryText(MaxRendezvousLength, out var text) is false
                || RendezvousAddress.TryParse(text, out var place, out _) is false)
                return Refuse("service illisible", out rejection);

            places.Add(place);
        }

        if (reader.TryText(MaxPasswordBytes, out var password) is false || password.Any(char.IsControl))
            return Refuse("mot de passe hors règles", out rejection);

        if (reader.TryU16(out var banCount) is false || banCount > MaxBans)
            return Refuse("trop de bannis", out rejection);

        var bans = new List<GroupBan>(banCount);

        for (var i = 0; i < banCount; i++)
        {
            if (reader.TryByte(out var flags) is false || flags is 0 or > (BanPeer | BanFingerprint))
                return Refuse("bannissement mal formé", out rejection);

            PeerId? peer = null;
            PlayerFingerprint? fingerprint = null;

            if ((flags & BanPeer) != 0)
            {
                if (reader.TryTake(PeerId.SizeInBytes, out var bytes) is false)
                    return Refuse("politique tronquée", out rejection);

                peer = PeerId.FromBytes(bytes);
            }

            if ((flags & BanFingerprint) != 0)
            {
                if (reader.TryTake(PlayerFingerprint.SizeInBytes, out var bytes) is false)
                    return Refuse("politique tronquée", out rejection);

                fingerprint = PlayerFingerprint.FromBytes(bytes);
            }

            bans.Add(new GroupBan(peer, fingerprint));
        }

        if (reader.TryByte(out var dissolved) is false || dissolved > 1)
            return Refuse("drapeau de dissolution mal formé", out rejection);

        if (reader.TryU16(out var attestationLength) is false || reader.TryTake(attestationLength, out var attestationBytes) is false)
            return Refuse("politique tronquée", out rejection);

        if (TryDecodeAttestation(attestationBytes, out var attestation, out rejection) is false)
            return false;

        if (reader.TryTake(KeyLength, out var signer) is false || reader.TryTake(SignatureLength, out var signature) is false)
            return Refuse("politique tronquée", out rejection);

        if (reader.Ended is false)
            return Refuse("octets en trop après la politique", out rejection);

        policy = new GroupPolicy(
            GroupId.FromBytes(group), version, name, code.ToArray(), places, password, bans, dissolved == 1,
            attestation!, signer.ToArray(), signature.ToArray());

        rejection = null;
        return true;
    }

    private static bool TryDecodeAttestation(ReadOnlySpan<byte> encoded, out GroupAttestation? attestation, out string? rejection)
    {
        attestation = null;
        var reader = new Reader(encoded);

        if (reader.TryByte(out var format) is false || format != Format)
            return Refuse("format d'attestation inconnu", out rejection);

        if (reader.TryTake(GroupId.SizeInBytes, out var group) is false
            || reader.TryU64(out var version) is false
            || reader.TryByte(out var admission) is false)
            return Refuse("attestation tronquée", out rejection);

        if (AdmissionMode.IsKnown(admission) is false)
            return Refuse("mode d'admission inconnu", out rejection);

        if (reader.TryTake(KeyLength, out var owner) is false)
            return Refuse("attestation tronquée", out rejection);

        if (reader.TryByte(out var count) is false || count > MaxModerators)
            return Refuse("trop de modérateurs", out rejection);

        var moderators = new List<byte[]>(count);

        for (var i = 0; i < count; i++)
        {
            if (reader.TryTake(KeyLength, out var key) is false)
                return Refuse("attestation tronquée", out rejection);

            moderators.Add(key.ToArray());
        }

        if (reader.TryTake(SignatureLength, out var signature) is false)
            return Refuse("attestation tronquée", out rejection);

        if (reader.Ended is false)
            return Refuse("octets en trop après l'attestation", out rejection);

        attestation = new GroupAttestation(
            GroupId.FromBytes(group), version, admission, owner.ToArray(), moderators, signature.ToArray());
        rejection = null;
        return true;
    }

    private static void WriteAttestationBody(ArrayBufferWriter<byte> writer, GroupAttestation attestation)
    {
        Byte(writer, Format);
        writer.Write(attestation.Group.ToBytes());
        U64(writer, attestation.Version);
        Byte(writer, attestation.Admission);
        writer.Write(attestation.Owner);
        Byte(writer, checked((byte)attestation.Moderators.Count));

        foreach (var moderator in attestation.Moderators)
            writer.Write(moderator);
    }

    private static void WritePolicyBody(ArrayBufferWriter<byte> writer, GroupPolicy policy)
    {
        Byte(writer, Format);
        writer.Write(policy.Group.ToBytes());
        U64(writer, policy.Version);
        Text(writer, policy.Name);
        writer.Write(policy.Code);
        Byte(writer, checked((byte)policy.Rendezvous.Count));

        foreach (var place in policy.Rendezvous)
            Text(writer, place.ToString());

        Text(writer, policy.Password);
        U16(writer, checked((ushort)policy.Bans.Count));

        foreach (var ban in policy.Bans)
        {
            Byte(writer, (byte)((ban.Peer is null ? 0 : BanPeer) | (ban.Fingerprint is null ? 0 : BanFingerprint)));

            if (ban.Peer is { } peer)
                writer.Write(peer.ToBytes());

            if (ban.Fingerprint is { } fingerprint)
                writer.Write(fingerprint.ToBytes());
        }

        Byte(writer, policy.Dissolved ? (byte)1 : (byte)0);

        var attestation = EncodeAttestation(policy.Attestation);
        U16(writer, checked((ushort)attestation.Length));
        writer.Write(attestation);
        writer.Write(policy.Signer);
    }

    private static void Byte(ArrayBufferWriter<byte> writer, byte value) => writer.Write([value]);

    private static void U16(ArrayBufferWriter<byte> writer, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void U64(ArrayBufferWriter<byte> writer, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        writer.Write(bytes);
    }

    private static void Text(ArrayBufferWriter<byte> writer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Byte(writer, checked((byte)bytes.Length));
        writer.Write(bytes);
    }

    private static bool Refuse(string why, out string? rejection)
    {
        rejection = why;
        return false;
    }

    /// <summary>Lecture bornée : chaque méthode rend faux plutôt que de lever sur une trame courte.</summary>
    private ref struct Reader
    {
        private ReadOnlySpan<byte> _rest;

        public Reader(ReadOnlySpan<byte> data) => _rest = data;

        public readonly bool Ended => _rest.IsEmpty;

        public bool TryTake(int count, out ReadOnlySpan<byte> bytes)
        {
            if (count < 0 || _rest.Length < count)
            {
                bytes = default;
                return false;
            }

            bytes = _rest[..count];
            _rest = _rest[count..];
            return true;
        }

        public bool TryByte(out byte value)
        {
            value = 0;

            if (TryTake(1, out var bytes) is false)
                return false;

            value = bytes[0];
            return true;
        }

        public bool TryU16(out ushort value)
        {
            value = 0;

            if (TryTake(2, out var bytes) is false)
                return false;

            value = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            return true;
        }

        public bool TryU64(out ulong value)
        {
            value = 0;

            if (TryTake(8, out var bytes) is false)
                return false;

            value = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            return true;
        }

        public bool TryText(int maxBytes, out string text)
        {
            text = "";

            if (TryByte(out var length) is false || length > maxBytes || TryTake(length, out var bytes) is false)
                return false;

            try
            {
                text = StrictUtf8.GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
