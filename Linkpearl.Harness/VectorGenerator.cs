using System.Text.Json;
using Linkpearl.Core.Crypto;

namespace Linkpearl.Harness;

/// <summary>
/// Produit les vecteurs figés du protocole.
/// </summary>
/// <remarks>
/// Ces vecteurs ne valident pas la construction, qui reste à faire relire : ils
/// détectent une dérive involontaire du format. Une étiquette HKDF renommée, un
/// champ déplacé dans l'en-tête, un nonce construit autrement, et le protocole
/// change sur le fil sans que rien ne casse à la compilation. C'est ce qu'ils
/// attrapent.
///
/// Ils ne couvrent que les parties déterministes : le handshake tire des
/// éphémères et des aléas, donc ses trames ne sont pas reproductibles.
/// </remarks>
public static class VectorGenerator
{
    public static string Build()
    {
        var sharedSecret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var transcript = Enumerable.Range(0, 32).Select(i => (byte)(0xA0 + i)).ToArray();
        var schedule = new HandshakeSchedule(sharedSecret, transcript);

        var sendKey = Enumerable.Range(0, 32).Select(i => (byte)(0x10 + i)).ToArray();
        var receiveKey = Enumerable.Range(0, 32).Select(i => (byte)(0x50 + i)).ToArray();
        var sessionId = Enumerable.Range(0, 16).Select(i => (byte)(0x90 + i)).ToArray();

        var channel = new SecureChannel(sendKey, receiveKey, sessionId);
        var frames = new List<object>();

        foreach (var (ch, kind, payload) in new[]
                 {
                     ((byte)0, (byte)0x01, "premier"u8.ToArray()),
                     ((byte)0, (byte)0x02, "deuxieme sur le meme canal"u8.ToArray()),
                     ((byte)23, (byte)0x08, new byte[64]),
                     ((byte)63, (byte)0x0C, Array.Empty<byte>()),
                 })
        {
            frames.Add(new
            {
                channel = ch,
                kind,
                payload = Convert.ToHexStringLower(payload),
                frame = Convert.ToHexStringLower(channel.Seal(ch, kind, payload)),
            });
        }

        return JsonSerializer.Serialize(new
        {
            note = "Vecteurs figes. Toute difference signale un changement de protocole sur le fil.",
            keySchedule = new
            {
                sharedSecret = Convert.ToHexStringLower(sharedSecret),
                transcript = Convert.ToHexStringLower(transcript),
                responderToInitiator = Convert.ToHexStringLower(schedule.ResponderToInitiatorKey),
                initiatorToResponder = Convert.ToHexStringLower(schedule.InitiatorToResponderKey),
                binding = Convert.ToHexStringLower(schedule.BindingKey),
                sessionId = Convert.ToHexStringLower(schedule.SessionId),
                dataInitiatorToResponder = Convert.ToHexStringLower(schedule.DataInitiatorToResponder),
                dataResponderToInitiator = Convert.ToHexStringLower(schedule.DataResponderToInitiator),
            },
            secureChannel = new
            {
                sendKey = Convert.ToHexStringLower(sendKey),
                receiveKey = Convert.ToHexStringLower(receiveKey),
                sessionId = Convert.ToHexStringLower(sessionId),
                frames,
            },
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
