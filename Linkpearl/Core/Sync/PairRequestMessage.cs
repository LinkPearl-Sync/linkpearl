using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Crypto;

namespace Linkpearl.Core.Sync;

/// <summary>Une demande de pairage, ou sa réponse.</summary>
/// <remarks>
/// Le nom de personnage voyage en clair dans la demande, et c'est le point de
/// toute la conception : c'est lui que le destinataire verra, et c'est lui qu'il
/// peut vérifier d'un coup d'œil en regardant qui est devant lui. Un nom
/// inventé ne correspondrait à personne de visible.
///
/// Chaque côté y joint une clé d'accord éphémère : le secret de paire vient de
/// leur accord, que le rendez-vous qui transporte les deux messages ne peut pas
/// calculer. Dans la version 1, le secret ne dépendait que de l'aléa, qui
/// voyageait en clair : tout service qui voyait passer le pairage le
/// connaissait.
/// </remarks>
public sealed record PairRequestMessage(
    bool IsAccept, byte[] PublicKey, byte[] PairingNonce, byte[] Ephemeral, string CharacterName, ushort WorldId)
{
    public const int NonceLength = 12;
    public const int MaxNameLength = 64;

    // Les types 0x01 et 0x02 étaient ceux de la version 1, sans éphémère. Ils
    // ne sont jamais réemployés : une demande d'un ancien client est refusée
    // avec un motif lisible, plutôt que lue de travers.
    private const byte KindRequestV1 = 0x01;
    private const byte KindAcceptV1 = 0x02;
    private const byte KindRequest = 0x03;
    private const byte KindAccept = 0x04;

    private const int KeyOffset = 1;
    private const int NonceOffset = KeyOffset + CryptoPrimitives.CompressedPointLength;
    private const int EphemeralOffset = NonceOffset + NonceLength;
    private const int WorldOffset = EphemeralOffset + CryptoPrimitives.CompressedPointLength;
    private const int HeaderLength = WorldOffset + sizeof(ushort);

    public byte[] Encode()
    {
        var name = Encoding.UTF8.GetBytes(CharacterName);

        if (name.Length > MaxNameLength)
            name = name[..MaxNameLength];

        var frame = new byte[HeaderLength + name.Length];

        frame[0] = IsAccept ? KindAccept : KindRequest;
        CryptoPrimitives.Compress(PublicKey).CopyTo(frame.AsSpan(KeyOffset));
        PairingNonce.CopyTo(frame.AsSpan(NonceOffset));
        CryptoPrimitives.Compress(Ephemeral).CopyTo(frame.AsSpan(EphemeralOffset));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(WorldOffset), WorldId);
        name.CopyTo(frame.AsSpan(HeaderLength));

        return frame;
    }

    public static bool TryDecode(ReadOnlySpan<byte> frame, out PairRequestMessage? message, out string? rejection)
    {
        message = null;

        if (frame.Length > 0 && frame[0] is KindRequestV1 or KindAcceptV1)
        {
            rejection = "demande d'un client d'avant le 24 septembre 2026 : l'autre joueur doit mettre à jour";
            return false;
        }

        if (frame.Length < HeaderLength)
        {
            rejection = "demande tronquée";
            return false;
        }

        if (frame[0] is not (KindRequest or KindAccept))
        {
            rejection = $"type de demande inconnu ({frame[0]:X2})";
            return false;
        }

        if (frame.Length - HeaderLength > MaxNameLength)
        {
            rejection = "nom de personnage trop long";
            return false;
        }

        byte[] publicKey;
        byte[] ephemeral;
        try
        {
            publicKey = CryptoPrimitives.Decompress(frame.Slice(KeyOffset, CryptoPrimitives.CompressedPointLength));
            ephemeral = CryptoPrimitives.Decompress(frame.Slice(EphemeralOffset, CryptoPrimitives.CompressedPointLength));
        }
        catch (CryptographicException e)
        {
            rejection = $"clé invalide : {e.Message}";
            return false;
        }

        var nonce = frame.Slice(NonceOffset, NonceLength).ToArray();
        var world = BinaryPrimitives.ReadUInt16BigEndian(frame[WorldOffset..]);

        // Le nom vient du réseau : on écarte tout ce qui n'est pas un nom de
        // personnage plausible, pour qu'aucune séquence de contrôle n'atteigne
        // l'interface.
        var name = Encoding.UTF8.GetString(frame[HeaderLength..]);

        if (name.Any(c => char.IsControl(c)))
        {
            rejection = "nom de personnage contenant des caractères de contrôle";
            return false;
        }

        message = new PairRequestMessage(frame[0] == KindAccept, publicKey, nonce, ephemeral, name, world);
        rejection = null;
        return true;
    }

    /// <summary>
    /// Le matériau du secret de paire : l'accord des deux éphémères, puis l'aléa.
    /// </summary>
    /// <remarks>
    /// L'aléa reste dans le matériau pour que deux pairages successifs des mêmes
    /// personnages donnent deux secrets distincts même si un éphémère se
    /// répétait. C'est l'accord qui porte le secret : l'aléa, lui, est public.
    /// </remarks>
    public static byte[] AgreeOnPairing(ECDiffieHellman ourEphemeral, ReadOnlySpan<byte> theirEphemeral, ReadOnlySpan<byte> pairingNonce)
    {
        var shared = CryptoPrimitives.Agree(ourEphemeral, theirEphemeral);

        try
        {
            return [.. shared, .. pairingNonce];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }
}
