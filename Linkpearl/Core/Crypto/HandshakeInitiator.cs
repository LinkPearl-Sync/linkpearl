using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Crypto;

/// <summary>
/// Côté initiateur d'un handshake SIGMA-I.
/// </summary>
/// <remarks>
/// SIGMA-I plutôt que Noise_IK : il n'existe aucune implémentation Noise en C#
/// managé pur et maintenue, et Noise_IK suppose que l'initiateur connaisse la
/// clé statique d'accord du répondeur, alors que notre identité est une clé de
/// signature. SIGMA sépare proprement l'accord de clé, qui donne la
/// confidentialité persistante, de l'authentification, qui signe le transcript.
///
/// Contrepartie assumée : Noise dispose de vecteurs de test externes, pas nous.
/// D'où les vecteurs figés dans le dépôt, et l'obligation de faire relire cette
/// construction par quelqu'un d'autre avant toute diffusion.
/// </remarks>
public sealed class HandshakeInitiator(ECDsa identity, IClock clock)
{
    private readonly ECDiffieHellman _ephemeral = CryptoPrimitives.GenerateEphemeral();
    private byte[]? _message1;

    public byte[] CreateMessage1()
    {
        var message = new byte[HandshakeFormat.Message1Length];

        message[0] = HandshakeFormat.VersionMajor;
        message[1] = HandshakeFormat.VersionMinor;
        BinaryPrimitives.WriteInt64BigEndian(
            message.AsSpan(HandshakeFormat.TimestampOffsetInMessage1), clock.UtcNow.ToUnixTimeSeconds());
        CryptoPrimitives.ExportPublicPoint(_ephemeral)
            .CopyTo(message.AsSpan(HandshakeFormat.EphemeralOffsetInMessage1));
        RandomNumberGenerator.Fill(message.AsSpan(HandshakeFormat.NonceOffsetInMessage1, HandshakeFormat.NonceLength));

        _message1 = message;
        return message;
    }

    public bool TryHandleMessage2(
        ReadOnlySpan<byte> message2, Func<byte[], bool> isAuthorized,
        out byte[]? message3, out SessionKeys? keys, out string? rejection)
    {
        message3 = null;
        keys = null;

        if (_message1 is null)
        {
            rejection = "message 2 reçu avant d'avoir émis le message 1";
            return false;
        }

        if (message2.Length != HandshakeFormat.Message2Length)
        {
            rejection = $"message 2 de taille inattendue ({message2.Length})";
            return false;
        }

        if (message2[0] != HandshakeFormat.VersionMajor)
        {
            rejection = $"version majeure incompatible ({message2[0]})";
            return false;
        }

        byte[] shared;
        try
        {
            shared = CryptoPrimitives.Agree(
                _ephemeral,
                message2.Slice(HandshakeFormat.EphemeralOffsetInMessage2, CryptoPrimitives.PublicPointLength));
        }
        catch (CryptographicException e)
        {
            rejection = $"éphémère du répondeur refusé : {e.Message}";
            return false;
        }

        var clear = message2[..HandshakeFormat.Message2ClearLength];
        var sealedAuthentication = message2[HandshakeFormat.Message2ClearLength..];

        var transcript1 = HandshakeTranscript.First(_message1, clear);
        var schedule = new HandshakeSchedule(shared, transcript1);

        if (HandshakeTranscript.TryOpenAuthentication(
                sealedAuthentication, HandshakeFormat.ResponderSignatureContext, transcript1,
                schedule.ResponderToInitiatorKey, schedule.BindingKey,
                isAuthorized, out var peerPublicKey, out rejection) is false)
            return false;

        var transcript2 = HandshakeTranscript.Second(transcript1, sealedAuthentication);

        message3 = HandshakeTranscript.SealAuthentication(
            identity, HandshakeFormat.InitiatorSignatureContext, transcript2,
            schedule.InitiatorToResponderKey, schedule.BindingKey);

        keys = new SessionKeys(
            SendKey: schedule.DataInitiatorToResponder,
            ReceiveKey: schedule.DataResponderToInitiator,
            SessionId: schedule.SessionId,
            PeerPublicKey: peerPublicKey);

        rejection = null;
        return true;
    }
}
