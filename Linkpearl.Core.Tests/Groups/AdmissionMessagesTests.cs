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

        var proof = RoundTrip(new AdmissionProof(Code, Nonce, Point(), new byte[AdmissionSealing.TagLength]));
        Assert.Equal(AdmissionSealing.TagLength, proof.Tag.Length);

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
    public void Un_nom_en_UTF8_invalide_est_refuse()
    {
        var encoded = AdmissionCodec.Encode(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "ab"));
        encoded[^1] = 0xFF; // 0xFF n'est un octet de tête d'aucune séquence UTF-8.
        Assert.False(AdmissionCodec.TryDecode(encoded, out _, out _));
    }

    [Fact]
    public void Un_nom_avec_une_marque_bidi_est_refuse()
    {
        // U+202E (RIGHT-TO-LEFT OVERRIDE) inverserait l'affichage du nom : ni un
        // caractère de contrôle, ni un espace, mais une catégorie Format à écarter.
        var hostile = AdmissionCodec.Encode(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, "Jha‮len"));
        Assert.False(AdmissionCodec.TryDecode(hostile, out _, out _));
    }

    [Fact]
    public void Un_nom_de_soixante_quatre_octets_tient_dans_la_boite_et_fait_l_aller_retour()
    {
        // « é » fait deux octets en UTF-8 : trente-deux fois deux, soixante-quatre
        // octets pile, la borne exacte de AdmissionCodec.MaxNameBytes.
        var name = new string('é', 32);
        Assert.Equal(AdmissionCodec.MaxNameBytes, Encoding.UTF8.GetByteCount(name));

        var request = RoundTrip(new AdmissionRequest(Code, Point(), Point(), Nonce, 21, name));
        Assert.Equal(name, request.CharacterName);
    }

    [Fact]
    public void La_preuve_ne_se_verifie_qu_avec_le_bon_mot_de_passe()
    {
        using var candidate = CryptoPrimitives.GenerateEphemeral();
        using var member = CryptoPrimitives.GenerateEphemeral();
        using var intruder = CryptoPrimitives.GenerateEphemeral();

        var candidatePoint = CryptoPrimitives.ExportPublicPoint(candidate);
        var memberPoint = CryptoPrimitives.ExportPublicPoint(member);
        var associated = AdmissionSealing.Associated(AdmissionKind.Proof, Nonce, memberPoint);

        // L'accord est symétrique : les deux côtés retrouvent la même étiquette
        // sans rien s'être échangé d'autre que leurs éphémères.
        var fromCandidate = AdmissionSealing.ProofTag(candidate, memberPoint, Nonce, "lune", associated);
        var fromMember = AdmissionSealing.ProofTag(member, candidatePoint, Nonce, "lune", associated);
        Assert.Equal(fromCandidate, fromMember);

        var wrongPassword = AdmissionSealing.ProofTag(member, candidatePoint, Nonce, "soleil", associated);
        Assert.NotEqual(fromMember, wrongPassword);

        // Un défieur qui n'est pas le vrai membre calcule un accord différent,
        // donc une étiquette différente, même avec le bon mot de passe.
        var fromIntruder = AdmissionSealing.ProofTag(intruder, candidatePoint, Nonce, "lune", associated);
        Assert.NotEqual(fromMember, fromIntruder);
    }

    [Fact]
    public void Le_scellement_de_la_bienvenue_ne_s_ouvre_qu_avec_la_bonne_paire_d_ephemeres()
    {
        using var candidate = CryptoPrimitives.GenerateEphemeral();
        using var member = CryptoPrimitives.GenerateEphemeral();
        using var intruder = CryptoPrimitives.GenerateEphemeral();

        var memberPoint = CryptoPrimitives.ExportPublicPoint(member);
        var candidatePoint = CryptoPrimitives.ExportPublicPoint(candidate);
        var associated = AdmissionSealing.Associated(AdmissionKind.Welcome, Nonce, memberPoint);

        var sealedGrant = AdmissionSealing.Seal(
            AdmissionSealing.Key(member, candidatePoint, Nonce, AdmissionSealing.Welcome), "bienvenue"u8, associated);

        var opened = AdmissionSealing.TryOpen(
            AdmissionSealing.Key(candidate, memberPoint, Nonce, AdmissionSealing.Welcome),
            sealedGrant, associated, out var plain);

        Assert.True(opened);
        Assert.Equal("bienvenue", Encoding.UTF8.GetString(plain));

        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(intruder, memberPoint, Nonce, AdmissionSealing.Welcome),
            sealedGrant, associated, out _));

        // Même accord, autre usage : une bienvenue ne s'ouvre pas comme une preuve.
        Assert.False(AdmissionSealing.TryOpen(
            AdmissionSealing.Key(candidate, memberPoint, Nonce, AdmissionSealing.Proof),
            sealedGrant, associated, out _));
    }

    [Fact]
    public void Une_bienvenue_scellee_ne_s_ouvre_pas_avec_d_autres_donnees_associees()
    {
        using var candidate = CryptoPrimitives.GenerateEphemeral();
        using var member = CryptoPrimitives.GenerateEphemeral();

        var memberPoint = CryptoPrimitives.ExportPublicPoint(member);
        var candidatePoint = CryptoPrimitives.ExportPublicPoint(candidate);
        var associated = AdmissionSealing.Associated(AdmissionKind.Welcome, Nonce, memberPoint);

        var sealedGrant = AdmissionSealing.Seal(
            AdmissionSealing.Key(member, candidatePoint, Nonce, AdmissionSealing.Welcome), "bienvenue"u8, associated);

        var key = AdmissionSealing.Key(candidate, memberPoint, Nonce, AdmissionSealing.Welcome);

        byte[] otherNonce = [.. Enumerable.Range(200, 12).Select(i => (byte)i)];
        var wrongNonceAssociated = AdmissionSealing.Associated(AdmissionKind.Welcome, otherNonce, memberPoint);
        Assert.False(AdmissionSealing.TryOpen(key, sealedGrant, wrongNonceAssociated, out _));

        var wrongKindAssociated = AdmissionSealing.Associated(AdmissionKind.Refusal, Nonce, memberPoint);
        Assert.False(AdmissionSealing.TryOpen(key, sealedGrant, wrongKindAssociated, out _));

        // Les bonnes données associées, elles, ouvrent toujours.
        Assert.True(AdmissionSealing.TryOpen(key, sealedGrant, associated, out var plain));
        Assert.Equal("bienvenue", Encoding.UTF8.GetString(plain));
    }

    [Fact]
    public void Un_octroi_fait_l_aller_retour_et_verifie_sa_propre_coherence()
    {
        using var groupKey = CryptoPrimitives.GenerateIdentity();
        var ownerKey = GroupPolicySigning.CompressedKey(groupKey);
        var grant = new GroupGrant(GroupId.Of(ownerKey), RandomNumberGenerator.GetBytes(32), ownerKey, "Compagnie");

        Assert.True(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(grant), out var back, out var why), why);
        Assert.Equal(grant.Group, back!.Group);
        Assert.Equal(grant.Secret, back.Secret);
        Assert.Equal("Compagnie", back.Name);

        // Ce contrôle prouve seulement qu'un octroi est cohérent avec sa propre
        // clé : une clé et un nom fabriqués de toutes pièces, mais cohérents
        // entre eux, le passeraient tout autant. Le lien entre le code
        // d'admission et le groupe qu'il ouvre n'est pas vérifiable par le
        // candidat : c'est un TOFU assumé, comme au pairage.
        var lying = grant with { Group = GroupId.FromBytes(new byte[16]) };
        Assert.False(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(lying), out _, out _));
    }

    [Fact]
    public void Un_octroi_dont_la_cle_n_est_pas_un_point_valide_est_refuse()
    {
        var badKey = new byte[CryptoPrimitives.CompressedPointLength];
        badKey[0] = 0x09; // ni 0x02 ni 0x03 : aucun point compressé ne commence ainsi.
        var grant = new GroupGrant(GroupId.Of(badKey), RandomNumberGenerator.GetBytes(32), badKey, "Compagnie");

        Assert.False(GroupGrantCodec.TryDecode(GroupGrantCodec.Encode(grant), out _, out _));
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
