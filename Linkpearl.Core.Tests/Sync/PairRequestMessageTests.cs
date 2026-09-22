using Linkpearl.Core.Crypto;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

public class PairRequestMessageTests
{
    private static PairRequestMessage Sample(string name = "Jhalen Tavari", bool accept = false)
    {
        using var identity = CryptoPrimitives.GenerateIdentity();

        return new PairRequestMessage(
            accept, CryptoPrimitives.ExportPublicPoint(identity),
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(PairRequestMessage.NonceLength),
            name, 21);
    }

    [Fact]
    public void Une_demande_fait_l_aller_retour()
    {
        var original = Sample();

        Assert.True(PairRequestMessage.TryDecode(original.Encode(), out var parsed, out var why), why);
        Assert.Equal(original.PublicKey, parsed!.PublicKey);
        Assert.Equal(original.PairingNonce, parsed.PairingNonce);
        Assert.Equal("Jhalen Tavari", parsed.CharacterName);
        Assert.Equal(21, parsed.WorldId);
        Assert.False(parsed.IsAccept);
    }

    [Fact]
    public void Une_acceptation_se_distingue_d_une_demande()
    {
        Assert.True(PairRequestMessage.TryDecode(Sample(accept: true).Encode(), out var parsed, out _));
        Assert.True(parsed!.IsAccept);
    }

    [Fact]
    public void Les_accents_d_un_nom_survivent()
    {
        Assert.True(PairRequestMessage.TryDecode(Sample("Ysée Tréville").Encode(), out var parsed, out var why), why);
        Assert.Equal("Ysée Tréville", parsed!.CharacterName);
    }

    [Fact]
    public void Un_nom_contenant_des_caracteres_de_controle_est_refuse()
    {
        // Le nom vient du réseau et finit dans l'interface : aucune séquence de
        // contrôle ne doit y arriver.
        var hostile = Sample("Jhalen\nTavari").Encode();

        Assert.False(PairRequestMessage.TryDecode(hostile, out _, out var why));
        Assert.Contains("contrôle", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_nom_trop_long_est_tronque_a_l_encodage_et_accepte_a_la_lecture()
    {
        var encoded = Sample(new string('a', 300)).Encode();

        Assert.True(PairRequestMessage.TryDecode(encoded, out var parsed, out var why), why);
        Assert.True(parsed!.CharacterName.Length <= PairRequestMessage.MaxNameLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(40)]
    public void Une_demande_tronquee_est_refusee_sans_lever(int length)
    {
        Assert.False(PairRequestMessage.TryDecode(new byte[length], out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_cle_publique_invalide_est_refusee()
    {
        var frame = Sample().Encode();
        frame.AsSpan(1, 32).Fill(0x01);

        Assert.False(PairRequestMessage.TryDecode(frame, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_type_inconnu_est_refuse()
    {
        var frame = Sample().Encode();
        frame[0] = 0x7F;

        Assert.False(PairRequestMessage.TryDecode(frame, out _, out var why));
        Assert.NotNull(why);
    }
}
