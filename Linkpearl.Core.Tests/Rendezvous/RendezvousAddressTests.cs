using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>
/// L'adresse d'un service de rendez-vous. Le port fait partie de l'adresse et
/// non d'un réglage global : la documentation recommande le 443 pour les
/// réseaux restrictifs, donc deux services peuvent écouter ailleurs.
/// </summary>
public class RendezvousAddressTests
{
    [Fact]
    public void Un_hote_seul_prend_le_port_par_defaut()
    {
        Assert.True(RendezvousAddress.TryParse("rdv.exemple.ch", out var address, out var why), why);
        Assert.Equal("rdv.exemple.ch", address.Host);
        Assert.Equal(RendezvousAddress.DefaultPort, address.Port);
    }

    [Fact]
    public void Un_port_explicite_est_retenu()
    {
        Assert.True(RendezvousAddress.TryParse("rdv.exemple.ch:443", out var address, out _));
        Assert.Equal(443, address.Port);
    }

    [Fact]
    public void Le_port_par_defaut_ne_s_ecrit_pas()
    {
        // Sans cela, la liste des services afficherait « :47900 » partout, et
        // l'utilisateur croirait à un réglage qu'il doit comprendre.
        Assert.Equal("rdv.exemple.ch", new RendezvousAddress("rdv.exemple.ch", 47900).ToString());
        Assert.Equal("rdv.exemple.ch:443", new RendezvousAddress("rdv.exemple.ch", 443).ToString());
    }

    [Fact]
    public void L_ecriture_se_relit()
    {
        var original = new RendezvousAddress("192.0.2.10", 8080);

        Assert.True(RendezvousAddress.TryParse(original.ToString(), out var back, out _));
        Assert.Equal(original, back);
    }

    [Theory]
    [InlineData(null, "adresse vide")]
    [InlineData("", "adresse vide")]
    [InlineData("   ", "adresse vide")]
    [InlineData("rdv.exemple.ch:0", "port")]
    [InlineData("rdv.exemple.ch:70000", "port")]
    [InlineData("rdv.exemple.ch:abc", "port")]
    [InlineData("rdv exemple.ch", "caractère")]
    [InlineData("http://rdv.exemple.ch", "caractère")]
    [InlineData("pair@rdv.exemple.ch", "caractère")]
    public void Une_adresse_malformee_est_refusee_avec_sa_raison(string? text, string expected)
    {
        Assert.False(RendezvousAddress.TryParse(text, out _, out var why));
        Assert.Contains(expected, why);
    }

    [Fact]
    public void Un_hote_demesure_est_refuse()
    {
        // Le libellé et l'hôte viennent du réseau dans la trame d'annuaire : la
        // borne est ici, avant que quoi que ce soit ne s'affiche.
        Assert.False(RendezvousAddress.TryParse(new string('a', 300), out _, out var why));
        Assert.Contains("trop long", why);
    }
}
