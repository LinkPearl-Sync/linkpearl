using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

/// <summary>
/// Les primitives viennent toutes de System.Security.Cryptography : P-256,
/// AES-GCM, HKDF et SHA-256 sont intégrés, accélérés matériellement, et
/// disponibles dès Windows 10. Ces tests vérifient surtout nos conversions,
/// qui sont l'endroit où l'on se trompe.
/// </summary>
public class CryptoPrimitivesTests
{
    [Fact]
    public void Une_cle_d_identite_s_exporte_et_se_relit()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var point = CryptoPrimitives.ExportPublicPoint(identity);

        Assert.Equal(CryptoPrimitives.PublicPointLength, point.Length);
        Assert.Equal(0x04, point[0]);   // point non compressé

        using var verifier = CryptoPrimitives.ImportVerifier(point);
        Assert.Equal(point, CryptoPrimitives.ExportPublicPoint(verifier));
    }

    [Fact]
    public void Une_signature_se_verifie_et_fait_toujours_la_meme_taille()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var signature = CryptoPrimitives.Sign(identity, "un message"u8);

        // Format concaténé et non DER : une signature DER a une taille variable,
        // ce qui compliquerait le format de trame pour rien.
        Assert.Equal(CryptoPrimitives.SignatureLength, signature.Length);

        using var verifier = CryptoPrimitives.ImportVerifier(CryptoPrimitives.ExportPublicPoint(identity));
        Assert.True(CryptoPrimitives.Verify(verifier, "un message"u8, signature));
    }

    [Fact]
    public void Une_signature_alteree_est_refusee()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var signature = CryptoPrimitives.Sign(identity, "un message"u8);
        signature[10] ^= 0x01;

        using var verifier = CryptoPrimitives.ImportVerifier(CryptoPrimitives.ExportPublicPoint(identity));
        Assert.False(CryptoPrimitives.Verify(verifier, "un message"u8, signature));
    }

    [Fact]
    public void Un_message_different_est_refuse()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var signature = CryptoPrimitives.Sign(identity, "un message"u8);

        using var verifier = CryptoPrimitives.ImportVerifier(CryptoPrimitives.ExportPublicPoint(identity));
        Assert.False(CryptoPrimitives.Verify(verifier, "un autre message"u8, signature));
    }

    [Fact]
    public void Une_autre_cle_ne_verifie_pas()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        using var autre = CryptoPrimitives.GenerateIdentity();
        var signature = CryptoPrimitives.Sign(identity, "un message"u8);

        using var verifier = CryptoPrimitives.ImportVerifier(CryptoPrimitives.ExportPublicPoint(autre));
        Assert.False(CryptoPrimitives.Verify(verifier, "un message"u8, signature));
    }

    [Fact]
    public void Les_deux_cotes_d_un_echange_ephemere_obtiennent_le_meme_secret()
    {
        using var a = CryptoPrimitives.GenerateEphemeral();
        using var b = CryptoPrimitives.GenerateEphemeral();

        var fromA = CryptoPrimitives.Agree(a, CryptoPrimitives.ExportPublicPoint(b));
        var fromB = CryptoPrimitives.Agree(b, CryptoPrimitives.ExportPublicPoint(a));

        Assert.Equal(fromA, fromB);
        Assert.Equal(32, fromA.Length);
    }

    [Fact]
    public void Deux_echanges_distincts_donnent_des_secrets_distincts()
    {
        using var a = CryptoPrimitives.GenerateEphemeral();
        using var b = CryptoPrimitives.GenerateEphemeral();
        using var c = CryptoPrimitives.GenerateEphemeral();

        Assert.NotEqual(
            CryptoPrimitives.Agree(a, CryptoPrimitives.ExportPublicPoint(b)),
            CryptoPrimitives.Agree(a, CryptoPrimitives.ExportPublicPoint(c)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(66)]
    public void Un_point_public_de_mauvaise_taille_est_refuse(int length)
    {
        Assert.Throws<CryptographicException>(() => CryptoPrimitives.ImportVerifier(new byte[length]));
    }

    [Fact]
    public void Un_point_public_hors_de_la_courbe_est_refuse()
    {
        // Un pair peut envoyer n'importe quoi : un point qui n'est pas sur la
        // courbe doit être rejeté et non produire un secret exploitable.
        var point = new byte[CryptoPrimitives.PublicPointLength];
        point[0] = 0x04;
        point[1] = 0x01;

        Assert.Throws<CryptographicException>(() => CryptoPrimitives.ImportVerifier(point));
    }

    [Fact]
    public void Le_chiffrement_authentifie_fait_l_aller_retour()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);

        var sealed_ = CryptoPrimitives.Seal(key, nonce, "secret"u8, "entete"u8);
        Assert.True(CryptoPrimitives.TryOpen(key, nonce, sealed_, "entete"u8, out var opened));
        Assert.Equal("secret"u8.ToArray(), opened);
    }

    [Fact]
    public void Une_donnee_associee_differente_fait_echouer_l_ouverture()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var sealed_ = CryptoPrimitives.Seal(key, nonce, "secret"u8, "entete"u8);

        Assert.False(CryptoPrimitives.TryOpen(key, nonce, sealed_, "autre"u8, out _));
    }

    [Fact]
    public void Un_texte_chiffre_altere_fait_echouer_l_ouverture_sans_lever()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var sealed_ = CryptoPrimitives.Seal(key, nonce, "secret"u8, "entete"u8);
        sealed_[2] ^= 0x01;

        // Sans lever : l'entrée vient du réseau, une exception remonterait dans
        // la boucle de synchronisation.
        Assert.False(CryptoPrimitives.TryOpen(key, nonce, sealed_, "entete"u8, out _));
    }

    [Fact]
    public void Un_texte_chiffre_trop_court_pour_porter_une_etiquette_est_refuse()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);

        Assert.False(CryptoPrimitives.TryOpen(key, nonce, new byte[8], "entete"u8, out _));
    }
}
