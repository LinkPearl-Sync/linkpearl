namespace Linkpearl.Core.Crypto;

/// <summary>
/// Disposition des trames du handshake, et étiquettes de domaine.
/// </summary>
/// <remarks>
/// Rassemblées ici plutôt que dispersées entre l'initiateur et le répondeur :
/// les deux côtés doivent calculer exactement le même transcript, et deux
/// constantes qui divergent donneraient un échec de signature impossible à
/// diagnostiquer.
///
/// Toute modification de ce fichier change le protocole sur le fil. Les
/// vecteurs figés de Fixtures/handshake-vectors.json sont là pour le signaler.
/// </remarks>
public static class HandshakeFormat
{
    public const byte VersionMajor = 1;
    public const byte VersionMinor = 0;

    public const int NonceLength = 16;

    /// <summary>Tolérance de l'horodatage de la première trame, dans les deux sens.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(60);

    // Message 1 : version(2) || horodatage(8) || éphémère(65) || aléa(16)
    public const int TimestampOffsetInMessage1 = 2;
    public const int EphemeralOffsetInMessage1 = 10;
    public const int NonceOffsetInMessage1 = EphemeralOffsetInMessage1 + CryptoPrimitives.PublicPointLength;
    public const int Message1Length = NonceOffsetInMessage1 + NonceLength;

    // Message 2 : version(2) || éphémère(65) || aléa(16) || scellé
    public const int EphemeralOffsetInMessage2 = 2;
    public const int NonceOffsetInMessage2 = EphemeralOffsetInMessage2 + CryptoPrimitives.PublicPointLength;
    public const int Message2ClearLength = NonceOffsetInMessage2 + NonceLength;

    /// <summary>Contenu scellé : identité(65) || signature(64) || liaison(32).</summary>
    public const int AuthenticationLength =
        CryptoPrimitives.PublicPointLength + CryptoPrimitives.SignatureLength + 32;

    public const int SealedAuthenticationLength = AuthenticationLength + CryptoPrimitives.TagLength;

    public const int Message2Length = Message2ClearLength + SealedAuthenticationLength;
    public const int Message3Length = SealedAuthenticationLength;

    public static ReadOnlySpan<byte> Prologue => "linkpearl-handshake-v1"u8;
    public static ReadOnlySpan<byte> ResponderSignatureContext => "linkpearl-sig-responder-v1"u8;
    public static ReadOnlySpan<byte> InitiatorSignatureContext => "linkpearl-sig-initiator-v1"u8;

    public static ReadOnlySpan<byte> InfoResponderToInitiator => "b->a"u8;
    public static ReadOnlySpan<byte> InfoInitiatorToResponder => "a->b"u8;
    public static ReadOnlySpan<byte> InfoBinding => "bind"u8;
    public static ReadOnlySpan<byte> InfoSessionId => "session id"u8;
    public static ReadOnlySpan<byte> InfoDataInitiatorToResponder => "data a->b"u8;
    public static ReadOnlySpan<byte> InfoDataResponderToInitiator => "data b->a"u8;

    /// <summary>
    /// Nonce des deux trames scellées du handshake.
    /// </summary>
    /// <remarks>
    /// Un nonce constant est sûr ici, et seulement ici : chaque clé de handshake
    /// est dérivée d'un accord éphémère neuf et ne chiffre qu'un seul message.
    /// Les deux sens utilisent des clés distinctes, donc il n'y a aucune
    /// réutilisation de couple clé-nonce. Le canal de données, lui, compte ses
    /// nonces ; voir SecureChannel.
    /// </remarks>
    public static ReadOnlySpan<byte> HandshakeNonce => new byte[CryptoPrimitives.NonceLength];
}
