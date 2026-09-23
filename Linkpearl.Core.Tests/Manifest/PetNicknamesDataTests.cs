using System.Text;
using Linkpearl.Core.Manifest;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class PetNicknamesDataTests
{
    private static string Encode(string separator, params string[] lines)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(string.Join(separator, lines)));

    private static readonly string[] Real =
    [
        "[PetNicknames(4)]", "Prénom Nom", "73", "18014398509481984",
        "[411^2,412^2]", "411^2^Sparky^<1, 0.5, 0>^null",
    ];

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void Nom_monde_et_contentid_sont_neutralises(string separator)
    {
        var clean = PetNicknamesData.Neutralize(Encode(separator, Real))!;
        var text = Encoding.Unicode.GetString(Convert.FromBase64String(clean));

        Assert.DoesNotContain("Prénom", text);
        Assert.DoesNotContain("18014398509481984", text);
        Assert.Contains("Sparky", text);
        Assert.True(PetNicknamesData.IsNeutralAndBounded(clean));
    }

    [Fact]
    public void Des_donnees_non_neutralisees_sont_refusees()
        => Assert.False(PetNicknamesData.IsNeutralAndBounded(Encode("\r\n", Real)));

    [Theory]
    [InlineData("pas du base64 !")]
    [InlineData("")]
    public void Une_donnee_illisible_donne_null_sans_exception(string input)
    {
        Assert.Null(PetNicknamesData.Neutralize(input));
        Assert.False(PetNicknamesData.IsNeutralAndBounded(input));
    }

    [Fact]
    public void Un_autre_en_tete_est_refuse()
        => Assert.Null(PetNicknamesData.Neutralize(Encode("\r\n", "[PetNicknames(3)]", "a", "1", "2")));

    [Fact]
    public void Trop_de_lignes_sont_refusees()
    {
        var lines = new[] { PetNicknamesData.Header, "a", "1", "2" }
            .Concat(Enumerable.Repeat("411^2^x^null^null", PetNicknamesData.MaxLines)).ToArray();

        Assert.Null(PetNicknamesData.Neutralize(Encode("\r\n", lines)));
    }

    [Fact]
    public void Une_ligne_trop_longue_est_refusee()
        => Assert.Null(PetNicknamesData.Neutralize(
            Encode("\r\n", PetNicknamesData.Header, "a", "1", "2", new string('x', PetNicknamesData.MaxLineLength + 1))));
}
