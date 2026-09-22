using System.Text.Json;
using Linkpearl.Core.Crypto;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

/// <summary>
/// Rejoue les vecteurs figés du dépôt.
/// </summary>
/// <remarks>
/// Ces vecteurs ne valident pas la construction : ils ont été produits par cette
/// même implémentation, donc ils n'attestent que de sa cohérence avec elle-même.
/// La validité de SIGMA-I tel qu'il est écrit ici doit être établie par une
/// relecture extérieure, et docs/protocol.md est fait pour cela.
///
/// Ce qu'ils attrapent, en revanche, est réel et fréquent : une étiquette HKDF
/// renommée, un champ déplacé dans l'en-tête, un nonce construit autrement. Rien
/// de tout cela ne casse à la compilation, et tout cela rend deux versions du
/// plugin incapables de se parler.
/// </remarks>
public class ProtocolVectorTests
{
    private static JsonElement Vectors()
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine("Fixtures", "protocol-vectors.json"))).RootElement;

    private static byte[] Hex(JsonElement element, string name)
        => Convert.FromHexString(element.GetProperty(name).GetString()!);

    [Fact]
    public void La_derivation_de_cles_n_a_pas_bouge()
    {
        var vectors = Vectors().GetProperty("keySchedule");
        var schedule = new HandshakeSchedule(Hex(vectors, "sharedSecret"), Hex(vectors, "transcript"));

        Assert.Equal(Hex(vectors, "responderToInitiator"), schedule.ResponderToInitiatorKey);
        Assert.Equal(Hex(vectors, "initiatorToResponder"), schedule.InitiatorToResponderKey);
        Assert.Equal(Hex(vectors, "binding"), schedule.BindingKey);
        Assert.Equal(Hex(vectors, "sessionId"), schedule.SessionId);
        Assert.Equal(Hex(vectors, "dataInitiatorToResponder"), schedule.DataInitiatorToResponder);
        Assert.Equal(Hex(vectors, "dataResponderToInitiator"), schedule.DataResponderToInitiator);
    }

    [Fact]
    public void Le_format_de_trame_du_canal_n_a_pas_booge()
    {
        var vectors = Vectors().GetProperty("secureChannel");
        var channel = new SecureChannel(
            Hex(vectors, "sendKey"), Hex(vectors, "receiveKey"), Hex(vectors, "sessionId"));

        foreach (var expected in vectors.GetProperty("frames").EnumerateArray())
        {
            var frame = channel.Seal(
                (byte)expected.GetProperty("channel").GetInt32(),
                (byte)expected.GetProperty("kind").GetInt32(),
                Convert.FromHexString(expected.GetProperty("payload").GetString()!));

            Assert.Equal(expected.GetProperty("frame").GetString(), Convert.ToHexStringLower(frame));
        }
    }

    [Fact]
    public void Les_trames_figees_se_relisent()
    {
        // Les clés sont inversées : ce que l'émetteur a scellé, le receveur l'ouvre.
        var vectors = Vectors().GetProperty("secureChannel");
        var receiver = new SecureChannel(
            Hex(vectors, "receiveKey"), Hex(vectors, "sendKey"), Hex(vectors, "sessionId"));

        foreach (var expected in vectors.GetProperty("frames").EnumerateArray())
        {
            var frame = Convert.FromHexString(expected.GetProperty("frame").GetString()!);

            Assert.True(receiver.TryOpen(frame, out var kind, out var channel, out var payload, out var why), why);
            Assert.Equal(expected.GetProperty("kind").GetInt32(), kind);
            Assert.Equal(expected.GetProperty("channel").GetInt32(), channel);
            Assert.Equal(expected.GetProperty("payload").GetString(), Convert.ToHexStringLower(payload));
        }
    }
}
