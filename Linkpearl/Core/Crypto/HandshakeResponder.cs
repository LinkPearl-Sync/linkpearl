using System.Buffers.Binary;
using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;

namespace Linkpearl.Core.Crypto;

/// <summary>Côté répondeur d'un handshake SIGMA-I.</summary>
public sealed class HandshakeResponder(ECDsa identity, IClock clock)
{
    private readonly ECDiffieHellman _ephemeral = CryptoPrimitives.GenerateEphemeral();

    private HandshakeSchedule? _schedule;
    private byte[]? _transcript2;

    public bool TryHandleMessage1(ReadOnlySpan<byte> message1, out byte[]? message2, out string? rejection)
    {
        message2 = null;

        if (message1.Length != HandshakeFormat.Message1Length)
        {
            rejection = $"message 1 de taille inattendue ({message1.Length})";
            return false;
        }

        if (message1[0] != HandshakeFormat.VersionMajor)
        {
            rejection = $"version majeure incompatible ({message1[0]})";
            return false;
        }

        var stamped = DateTimeOffset.FromUnixTimeSeconds(
            BinaryPrimitives.ReadInt64BigEndian(message1[HandshakeFormat.TimestampOffsetInMessage1..]));

        // Anti-rejeu : une trame trop ancienne ou venue du futur est refusée. La
        // tolérance vaut dans les deux sens, les horloges dérivant des deux
        // côtés.
        var drift = clock.UtcNow - stamped;
        if (drift.Duration() > HandshakeFormat.ClockTolerance)
        {
            rejection = $"horodatage hors tolérance ({drift.TotalSeconds:F0} s d'écart)";
            return false;
        }

        byte[] shared;
        try
        {
            shared = CryptoPrimitives.Agree(
                _ephemeral,
                message1.Slice(HandshakeFormat.EphemeralOffsetInMessage1, CryptoPrimitives.PublicPointLength));
        }
        catch (CryptographicException e)
        {
            rejection = $"éphémère de l'initiateur refusé : {e.Message}";
            return false;
        }

        var clear = new byte[HandshakeFormat.Message2ClearLength];
        clear[0] = HandshakeFormat.VersionMajor;
        clear[1] = HandshakeFormat.VersionMinor;
        CryptoPrimitives.ExportPublicPoint(_ephemeral)
            .CopyTo(clear.AsSpan(HandshakeFormat.EphemeralOffsetInMessage2));
        RandomNumberGenerator.Fill(clear.AsSpan(HandshakeFormat.NonceOffsetInMessage2, HandshakeFormat.NonceLength));

        var transcript1 = HandshakeTranscript.First(message1, clear);
        _schedule = new HandshakeSchedule(shared, transcript1);

        var sealedAuthentication = HandshakeTranscript.SealAuthentication(
            identity, HandshakeFormat.ResponderSignatureContext, transcript1,
            _schedule.ResponderToInitiatorKey, _schedule.BindingKey);

        _transcript2 = HandshakeTranscript.Second(transcript1, sealedAuthentication);

        var full = new byte[HandshakeFormat.Message2Length];
        clear.CopyTo(full.AsSpan());
        sealedAuthentication.CopyTo(full.AsSpan(HandshakeFormat.Message2ClearLength));

        message2 = full;
        rejection = null;
        return true;
    }

    public bool TryHandleMessage3(
        ReadOnlySpan<byte> message3, Func<byte[], bool> isAuthorized, out SessionKeys? keys, out string? rejection)
    {
        keys = null;

        if (_schedule is null || _transcript2 is null)
        {
            rejection = "message 3 reçu avant le message 1";
            return false;
        }

        if (message3.Length != HandshakeFormat.Message3Length)
        {
            rejection = $"message 3 de taille inattendue ({message3.Length})";
            return false;
        }

        if (HandshakeTranscript.TryOpenAuthentication(
                message3, HandshakeFormat.InitiatorSignatureContext, _transcript2,
                _schedule.InitiatorToResponderKey, _schedule.BindingKey,
                isAuthorized, out var peerPublicKey, out rejection) is false)
            return false;

        keys = new SessionKeys(
            SendKey: _schedule.DataResponderToInitiator,
            ReceiveKey: _schedule.DataInitiatorToResponder,
            SessionId: _schedule.SessionId,
            PeerPublicKey: peerPublicKey);

        rejection = null;
        return true;
    }
}
