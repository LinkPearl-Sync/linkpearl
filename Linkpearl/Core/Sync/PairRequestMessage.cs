using System.Buffers.Binary;
using System.Text;
using Linkpearl.Core.Crypto;

namespace Linkpearl.Core.Sync;

/// <summary>Une demande de pairage, ou sa réponse.</summary>
/// <remarks>
/// Le nom de personnage voyage en clair dans la demande, et c'est le point de
/// toute la conception : c'est lui que le destinataire verra, et c'est lui qu'il
/// peut vérifier d'un coup d'œil en regardant qui est devant lui. Un nom
/// inventé ne correspondrait à personne de visible.
/// </remarks>
public sealed record PairRequestMessage(
    bool IsAccept, byte[] PublicKey, byte[] PairingNonce, string CharacterName, ushort WorldId)
{
    public const int NonceLength = 12;
    public const int MaxNameLength = 64;

    private const byte KindRequest = 0x01;
    private const byte KindAccept = 0x02;

    public byte[] Encode()
    {
        var name = Encoding.UTF8.GetBytes(CharacterName);

        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];

        var frame = new byte[1 + CryptoPrimitives.CompressedPointLength + NonceLength + sizeof(ushort) + name.Length];
        var offset = 0;

        frame[offset++] = IsAccept ? KindAccept : KindRequest;
        CryptoPrimitives.Compress(PublicKey).CopyTo(frame.AsSpan(offset));
        offset += CryptoPrimitives.CompressedPointLength;
        PairingNonce.CopyTo(frame.AsSpan(offset));
        offset += NonceLength;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(offset), WorldId);
        offset += sizeof(ushort);
        name.CopyTo(frame.AsSpan(offset));

        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, out PairRequestMessage? message, out string? rejection)
    {
        message = null;

        var header = 1 + CryptoPrimitives.CompressedPointLength + NonceLength + sizeof(ushort);

        if (frame.Length < header)
        {
            rejection = "demande tronquée";
            return false;
        }

        if (frame[0] is not (KindRequest or KindAccept))
        {
            rejection = $"type de demande inconnu ({frame[0]:X2})";
            return false;
        }

        if (frame.Length - header > MaxNameLength)
        {
            rejection = "nom de personnage trop long";
            return false;
        }

        byte[] publicKey;
        try
        {
            publicKey = CryptoPrimitives.Decompress(frame.Slice(1, CryptoPrimitives.CompressedPointLength));
        }
        catch (System.Security.Cryptography.CryptographicException e)
        {
            rejection = $"clé publique invalide : {e.Message}";
            return false;
        }

        var nonce = frame.Slice(1 + CryptoPrimitives.CompressedPointLength, NonceLength).ToArray();
        var world = BinaryPrimitives.ReadUInt16BigEndian(frame[(1 + CryptoPrimitives.CompressedPointLength + NonceLength)..]);

        // Le nom vient du réseau : on écarte tout ce qui n'est pas un nom de
        // personnage plausible, pour qu'aucune séquence de contrôle n'atteigne
        // l'interface.
        var name = Encoding.UTF8.GetString(frame[header..]);

        if (name.Any(c => char.IsControl(c)))
        {
            rejection = "nom de personnage contenant des caractères de contrôle";
            return false;
        }

        message = new PairRequestMessage(frame[0] == KindAccept, publicKey, nonce, name, world);
        rejection = null;
        return true;
    }
}
