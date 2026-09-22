using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

public class InvitationTicketTests
{
    [Fact]
    public void Un_ticket_fait_exactement_douze_caracteres()
    {
        for (var i = 0; i < 200; i++)
            Assert.Equal(InvitationTicket.Length, InvitationTicket.Create().Encode().Length);
    }

    [Fact]
    public void Un_ticket_fait_l_aller_retour()
    {
        for (var i = 0; i < 200; i++)
        {
            var original = InvitationTicket.Create();

            Assert.True(InvitationTicket.TryParse(original.Encode(), out var parsed, out var why), why);
            Assert.Equal(original, parsed);
        }
    }

    [Fact]
    public void Un_ticket_fait_l_aller_retour_binaire()
    {
        var original = InvitationTicket.Create();

        Assert.Equal(InvitationTicket.SizeInBytes, original.ToBytes().Length);
        Assert.Equal(original, InvitationTicket.FromBytes(original.ToBytes()));
    }

    [Fact]
    public void Deux_tickets_ne_se_repetent_pas()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 5000; i++)
            Assert.True(seen.Add(InvitationTicket.Create().Encode()));
    }

    [Fact]
    public void La_casse_n_a_pas_d_importance()
    {
        var text = InvitationTicket.Create().Encode();

        Assert.True(InvitationTicket.TryParse(text.ToLowerInvariant(), out var a, out _));
        Assert.True(InvitationTicket.TryParse(text.ToUpperInvariant(), out var b, out _));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Les_tirets_et_espaces_de_confort_sont_ignores()
    {
        var text = InvitationTicket.Create().Encode();
        var espace = $"{text[..4]} {text[4..8]} {text[8..]}";
        var tirets = $"{text[..4]}-{text[4..8]}-{text[8..]}";

        Assert.True(InvitationTicket.TryParse(espace, out var a, out var why1), why1);
        Assert.True(InvitationTicket.TryParse(tirets, out var b, out var why2), why2);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Une_faute_de_recopie_se_distingue_d_un_ticket_expire()
    {
        // Deux messages d'erreur très différents à lire : « vous avez mal
        // recopié » et « ce ticket n'existe plus ».
        var text = InvitationTicket.Create().Encode().ToCharArray();
        text[5] = text[5] == 'X' ? 'Y' : 'X';

        var altered = new string(text);

        if (InvitationTicket.TryParse(altered, out _, out var why) is false)
            Assert.Contains("recopié", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_somme_de_controle_attrape_la_grande_majorite_des_fautes()
    {
        var detected = 0;
        var total = 0;

        for (var i = 0; i < 500; i++)
        {
            var text = InvitationTicket.Create().Encode().ToCharArray();
            var position = Random.Shared.Next(text.Length);
            var replacement = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[Random.Shared.Next(32)];

            if (text[position] == replacement)
                continue;

            text[position] = replacement;
            total++;

            if (InvitationTicket.TryParse(new string(text), out _, out _) is false)
                detected++;
        }

        // Douze bits de contrôle : on attend environ 99,98 % de détection.
        Assert.True(detected >= total * 0.99, $"{detected} sur {total} détectées");
    }

    [Theory]
    [InlineData("")]
    [InlineData("TROPCOURT")]
    [InlineData("BEAUCOUPTROPLONGPOURUNTICKET")]
    [InlineData("!!!!!!!!!!!!")]
    public void Un_ticket_malforme_est_refuse_sans_lever(string text)
    {
        Assert.False(InvitationTicket.TryParse(text, out _, out var why));
        Assert.NotNull(why);
    }
}
