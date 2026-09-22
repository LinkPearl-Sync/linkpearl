using System.Security.Cryptography;

namespace Linkpearl.Core.Crypto;

/// <summary>
/// Dérivation des clés à partir du secret d'accord et du transcript.
/// </summary>
/// <remarks>
/// Le transcript entre dans le sel de l'extraction : deux sessions qui
/// partageraient par accident le même secret d'accord, mais pas les mêmes
/// aléas, produiraient malgré tout des clés distinctes.
/// </remarks>
internal sealed class HandshakeSchedule
{
    private readonly byte[] _prk;

    public HandshakeSchedule(ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> transcript1)
    {
        // Surcharges à destination explicite : les surcharges qui rendent un
        // tableau n'acceptent pas de span, et copier pour les satisfaire
        // laisserait des clés supplémentaires traîner en mémoire.
        _prk = new byte[32];
        HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, transcript1, _prk);
    }

    public byte[] ResponderToInitiatorKey => Expand(HandshakeFormat.InfoResponderToInitiator, 32);
    public byte[] InitiatorToResponderKey => Expand(HandshakeFormat.InfoInitiatorToResponder, 32);
    public byte[] BindingKey => Expand(HandshakeFormat.InfoBinding, 32);
    public byte[] SessionId => Expand(HandshakeFormat.InfoSessionId, 16);
    public byte[] DataInitiatorToResponder => Expand(HandshakeFormat.InfoDataInitiatorToResponder, 32);
    public byte[] DataResponderToInitiator => Expand(HandshakeFormat.InfoDataResponderToInitiator, 32);

    private byte[] Expand(ReadOnlySpan<byte> info, int length)
    {
        var output = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, _prk, output, info);
        return output;
    }
}
