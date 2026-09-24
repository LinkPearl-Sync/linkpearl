using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Groups;

public sealed class AdmissionMessagesTests
{
    private static readonly byte[] Code = [1, 2, 3, 4, 5, 6];
    private static readonly byte[] Nonce = [.. Enumerable.Range(100, 12).Select(i => (byte)i)];
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Point()
    {
        using var key = CryptoPrimitives.GenerateEphemeral();
        return CryptoPrimitives.ExportPublicPoint(key);
    }

    private static T RoundTrip<T>(T message) where T : AdmissionMessage
    {
        var encoded = AdmissionCodec.Encode(message);
        Assert.True(encoded.Length <= RendezvousWire.MaxDepositLength);
        Assert.True(AdmissionCodec.TryDecode(encoded, out var decoded, out var why), why);
        return Assert.IsType<T>(decoded);
    }

    [Fact]
    public void Les_vecteurs_figes_sont_retrouves()
    {
        Assert.Equal("e5153db1e51e", GroupDerivation.AdmissionAddress(Code, Noon).ToString());

        byte[] shared = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
        Assert.Equal("8341723f2280566dcb34344d5eded1c5f25eb0287a702e68c529c3067fa1b482",
            Convert.ToHexStringLower(AdmissionSealing.DeriveKey(shared, Nonce, AdmissionSealing.Proof)));
        Assert.Equal("657e3195b920ee89df8d42bf96d75de2a3ab0f79a807a4e4305d1db51250c0ac",
            Convert.ToHexStringLower(AdmissionSealing.DeriveKey(shared, Nonce, AdmissionSealing.Welcome)));
    }

    [Fact]
    public void Chaque_depot_fait_l_aller_retour()
    {
        var request = RoundTrip(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "Jhalen Tavari"));
        Assert.Equal("Jhalen Tavari", request.CharacterName);
        Assert.Equal(21, request.WorldId);
        Assert.Equal(Code, request.Code);

        var challenge = RoundTrip(new AdmissionChallenge(Nonce, Point()));
        Assert.Equal(Nonce, challenge.Nonce);

        var proof = RoundTrip(new AdmissionProof(Code, Nonce, Point(), new byte[16 + GroupPolicyCodec.MaxPasswordBytes]));
        Assert.Equal(16 + GroupPolicyCodec.MaxPasswordBytes, proof.SealedPassword.Length);

        var welcome = RoundTrip(new AdmissionWelcome(Nonce, Point(), new byte[GroupGrantCodec.MaxSealedLength]));
        Assert.Equal(GroupGrantCodec.MaxSealedLength, welcome.SealedGrant.Length);

        var refusal = RoundTrip(new AdmissionRefusal(Nonce, RefusalReason.Declined));
        Assert.Equal(RefusalReason.Declined, refusal.Reason);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x05, 1, 2 })]
    [InlineData(new byte[] { 0x03 })]
    [InlineData(new byte[] { 0x7F, 0, 0, 0 })]
    public void Un_depot_mal_forme_est_refuse(byte[] payload)
        => Assert.False(AdmissionCodec.TryDecode(payload, out _, out _));

    [Fact]
    public void Un_nom_hostile_ou_un_motif_inconnu_est_refuse()
    {
        var hostile = AdmissionCodec.Encode(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "Jhalen\nTavari"));
        Assert.False(AdmissionCodec.TryDecode(hostile, out _, out _));

        var unknown = AdmissionCodec.Encode(new AdmissionRefusal(Nonce, RefusalReason.Declined));
        unknown[^1] = 0x7F;
        Assert.False(AdmissionCodec.TryDecode(unknown, out _, out _));

        var tail = AdmissionCodec.Encode(new AdmissionChallenge(Nonce, Point()));
        Assert.False(AdmissionCodec.TryDecode([.. tail, 0x00], out _, out _));
    }

    [Fact]
    public void Le_scellement_ne_s_ouvre_qu_avec_la_bonne_paire_d_ephemeres()
    {
        using var candidate = CryptoPrimitives.GenerateEphemeral();
        using var member = CryptoPrimitives.GenerateEphemeral();
        using var intruder = CryptoPrimitives.GenerateEphemeral();

        var memberPoint = CryptoPrimitives.ExportPublicPoint(member);
        var associated = AdmissionSealing.Associated(AdmissionKind.Proof, Nonce, memberPoint);

        var sealedPassword = AdmissionSealing.Seal(
            AdmissionSealing.Key(candidate, memberPoint, Nonce, AdmissionSealing.Proof), "lune"u8, associated);

        var opened = AdmissionSealing.TryOpen(
            AdmissionSealing.Key(member, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Proof),
            sealedPassword, associated, out var plain);

        Assert.True(opened);
        Assert.Equal("lune", Encoding.UTF8.GetString(plain));

        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(intruder, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Proof),
            sealedPassword, associated, out _));

        // Même accord, autre usage : une preuve ne s'ouvre pas comme une bienvenue.
        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(member, CryptoPrimitives.ExportPublicPoint(candidate), Nonce, AdmissionSealing.Welcome),
            sealedPassword, associated, out _));
    }

    [Fact]
    public void Un_octroi_fait_l_aller_retour_et_verifie_son_groupe()
    {
        using var groupKey = CryptoPrimitives.GenerateIdentity();
        var ownerKey = GroupPolicySigning.CompressedKey(groupKey);
        var grant = new GroupGrant(GroupId.Of(ownerKey), RandomNumberGenerator.GetBytes(32), ownerKey, "Compagnie");

        Assert.True(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(grant), out var back, out var why), why);
        Assert.Equal(grant.Group, back!.Group);
        Assert.Equal(grant.Secret, back.Secret);
        Assert.Equal("Compagnie", back.Name);

        // Un identifiant qui ne dérive pas de la clé du groupe : un membre
        // malveillant qui voudrait faire entrer le candidat ailleurs.
        var lying = grant with { Group = GroupId.FromBytes(new byte[16]) };
        Assert.False(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(lying), out _, out _));
    }

    [Fact]
    public void La_boite_d_admission_tourne_avec_la_fenetre_et_depend_du_code()
    {
        Assert.Equal(
            [GroupDerivation.AdmissionAddress(Code, Noon), GroupDerivation.AdmissionAddress(Code, Noon, 1)],
            GroupDerivation.AdmissionAround(Code, Noon));

        Assert.NotEqual(GroupDerivation.AdmissionAddress(Code, Noon), GroupDerivation.AdmissionAddress([9, 9, 9, 9, 9, 9], Noon));
    }
}
