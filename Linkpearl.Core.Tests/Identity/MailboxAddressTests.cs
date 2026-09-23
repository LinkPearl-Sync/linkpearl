using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

public class MailboxAddressTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static PlayerFingerprint Print(string name) => PlayerFingerprint.Of(name, 21);

    [Fact]
    public void Deux_personnes_calculent_la_meme_adresse_pour_le_meme_personnage()
    {
        // C'est toute la fonction : quiconque voit le personnage sait où lui
        // adresser une demande, sans rien avoir échangé au préalable.
        Assert.Equal(
            MailboxAddress.Of(Print("jhalen"), T0),
            MailboxAddress.Of(Print("jhalen"), T0));
    }

    [Fact]
    public void Deux_personnages_ont_des_adresses_differentes()
    {
        Assert.NotEqual(
            MailboxAddress.Of(Print("jhalen"), T0),
            MailboxAddress.Of(Print("autre"), T0));
    }

    [Fact]
    public void Deux_homonymes_sur_deux_mondes_ont_des_adresses_differentes()
    {
        Assert.NotEqual(
            MailboxAddress.Of(PlayerFingerprint.Of("jhalen", 21), T0),
            MailboxAddress.Of(PlayerFingerprint.Of("jhalen", 36), T0));
    }

    [Fact]
    public void L_adresse_change_de_fenetre_en_fenetre()
    {
        // Un observateur ne peut donc pas relier deux périodes entre elles. Il
        // peut en revanche deviner un nom dans une fenêtre donnée, et c'est le
        // prix assumé de la découvrabilité.
        Assert.NotEqual(
            MailboxAddress.Of(Print("jhalen"), T0),
            MailboxAddress.Of(Print("jhalen"), T0 + MailboxAddress.Window));
    }

    [Fact]
    public void L_adresse_ne_change_pas_a_l_interieur_d_une_fenetre()
    {
        Assert.Equal(
            MailboxAddress.Of(Print("jhalen"), T0),
            MailboxAddress.Of(Print("jhalen"), T0.AddMinutes(29)));
    }

    [Fact]
    public void On_surveille_la_fenetre_courante_et_la_suivante()
    {
        var around = MailboxAddress.Around(Print("jhalen"), T0);

        Assert.Equal(2, around.Count);
        Assert.Contains(MailboxAddress.Of(Print("jhalen"), T0 + MailboxAddress.Window), around);
    }

    [Fact]
    public void L_adresse_fait_l_aller_retour_binaire()
    {
        var original = MailboxAddress.Of(Print("jhalen"), T0);

        Assert.Equal(MailboxAddress.SizeInBytes, original.ToBytes().Length);
        Assert.Equal(original, MailboxAddress.FromBytes(original.ToBytes()));
    }

    [Fact]
    public void La_fenetre_change_toutes_les_trente_minutes()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        Assert.Equal(MailboxAddress.IndexAt(start), MailboxAddress.IndexAt(start.AddMinutes(29)));
        Assert.NotEqual(MailboxAddress.IndexAt(start), MailboxAddress.IndexAt(start.AddMinutes(31)));
    }

    [Fact]
    public void Une_boite_ouverte_une_fois_pour_toutes_cesse_detre_trouvable()
    {
        // Ce que le service garde, ce sont les adresses ouvertes ; ce que les
        // autres calculent, c'est l'adresse de la fenêtre courante. Passé la
        // fenêtre suivante, les deux ne se rencontrent plus, et c'est pourquoi
        // la présence rouvre ses boîtes au changement de fenêtre.
        var fingerprint = PlayerFingerprint.Of("nom fictif", 73);
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var later = start.AddMinutes(75);

        var opened = MailboxAddress.Around(fingerprint, start).Select(a => a.ToString()).ToHashSet();

        Assert.DoesNotContain(MailboxAddress.Of(fingerprint, later).ToString(), opened);
        Assert.Contains(MailboxAddress.Of(fingerprint, start.AddMinutes(45)).ToString(), opened);
    }
}
