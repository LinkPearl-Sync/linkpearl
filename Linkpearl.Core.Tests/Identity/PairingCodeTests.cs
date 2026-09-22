using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

/// <summary>
/// Le code d'invitation porte la clé publique complète. C'est ce qui empêche le
/// service de rendez-vous de substituer une identité : il ne peut au pire que
/// refuser son service.
/// </summary>
public class PairingCodeTests
{
    private static PairingCode Sample()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        return PairingCode.Create(CryptoPrimitives.ExportPublicPoint(identity), "rdv.exemple.ch");
    }

    [Fact]
    public void Un_code_fait_l_aller_retour()
    {
        var original = Sample();
        var text = original.Encode();

        Assert.True(PairingCode.TryParse(text, out var parsed, out var why), why);
        Assert.Equal(original.PublicKey, parsed!.PublicKey);
        Assert.Equal(original.PairingNonce, parsed.PairingNonce);
        Assert.Equal("rdv.exemple.ch", parsed.RendezvousHost);
    }

    [Fact]
    public void Le_code_porte_le_serveur_de_rendez_vous()
    {
        // Sans lui, deux pairs configurés sur deux rendez-vous différents ne se
        // trouveraient jamais, et personne ne comprendrait pourquoi.
        Assert.EndsWith("@rdv.exemple.ch", Sample().Encode(), StringComparison.Ordinal);
    }

    [Fact]
    public void Le_code_reste_raisonnablement_court()
    {
        var text = Sample().Encode();
        Assert.InRange(text.Length, 80, 130);
    }

    [Fact]
    public void Deux_codes_de_la_meme_cle_different_par_leur_alea()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var key = CryptoPrimitives.ExportPublicPoint(identity);

        Assert.NotEqual(
            PairingCode.Create(key, "rdv.exemple.ch").Encode(),
            PairingCode.Create(key, "rdv.exemple.ch").Encode());
    }

    [Fact]
    public void La_casse_n_a_pas_d_importance()
    {
        var text = Sample().Encode();

        Assert.True(PairingCode.TryParse(text.ToLowerInvariant(), out var minuscules, out _));
        Assert.True(PairingCode.TryParse(text.ToUpperInvariant(), out var majuscules, out _));
        Assert.Equal(minuscules!.PublicKey, majuscules!.PublicKey);
    }

    [Theory]
    [InlineData('I', '1')]
    [InlineData('l', '1')]
    [InlineData('O', '0')]
    public void Les_caracteres_ambigus_a_l_oreille_sont_tolERES(char ambiguous, char intended)
    {
        // Crockford écarte I, L, O et U de l'alphabet. On accepte quand même
        // celui qui les a entendus et écrits, plutôt que de lui dire « code
        // invalide » sans explication.
        var text = Sample().Encode();
        var index = text.IndexOf(intended, StringComparison.Ordinal);

        if (index < 4)
            return;   // pas de caractère correspondant dans cet échantillon

        var altered = text.ToCharArray();
        altered[index] = ambiguous;

        Assert.True(PairingCode.TryParse(new string(altered), out _, out var why), why);
    }

    [Fact]
    public void Une_faute_de_frappe_est_detectee_avant_toute_operation_reseau()
    {
        // Sans somme de contrôle, une faute donnerait « échec de connexion »
        // plusieurs secondes plus tard, ce qui n'aide personne.
        var text = Sample().Encode();
        var index = text.Length / 2;
        var altered = text.ToCharArray();
        altered[index] = altered[index] == 'X' ? 'Y' : 'X';

        Assert.False(PairingCode.TryParse(new string(altered), out _, out var why));
        Assert.Contains("contrôle", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("n'importe quoi")]
    [InlineData("LP1-ABCDEF")]
    [InlineData("LP1-ABCDEF@")]
    [InlineData("ABCDEF@rdv.exemple.ch")]
    public void Un_code_malforme_est_refuse_sans_lever(string text)
    {
        Assert.False(PairingCode.TryParse(text, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_version_inconnue_est_refusee_avec_un_message_utile()
    {
        var text = Sample().Encode();
        Assert.False(PairingCode.TryParse("LP9" + text[3..], out _, out var why));
        Assert.Contains("version", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Les_tirets_de_groupement_sont_ignores_a_la_lecture()
    {
        var text = Sample().Encode();
        var sansTirets = "LP1-" + text[4..].Replace("-", "");

        Assert.True(PairingCode.TryParse(sansTirets, out _, out var why), why);
    }

    [Fact]
    public void L_identifiant_de_pair_derive_de_la_cle_publique()
    {
        using var identity = CryptoPrimitives.GenerateIdentity();
        var key = CryptoPrimitives.ExportPublicPoint(identity);

        var a = PairingCode.Create(key, "a.exemple.ch");
        var b = PairingCode.Create(key, "b.exemple.ch");

        // Deux invitations de la même clé désignent le même pair, quel que soit
        // le rendez-vous et quel que soit l'aléa.
        Assert.Equal(PeerId.Of(a.PublicKey), PeerId.Of(b.PublicKey));
    }

    [Fact]
    public void Deux_cles_differentes_donnent_deux_identifiants_differents()
    {
        using var a = CryptoPrimitives.GenerateIdentity();
        using var b = CryptoPrimitives.GenerateIdentity();

        Assert.NotEqual(
            PeerId.Of(CryptoPrimitives.ExportPublicPoint(a)),
            PeerId.Of(CryptoPrimitives.ExportPublicPoint(b)));
    }
}
