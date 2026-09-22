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
}
