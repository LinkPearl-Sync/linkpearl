using System.Security.Cryptography;
using System.Text;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public class ExtrasValidatorTests
{
    private static readonly Quotas Q = Quotas.Default;

    private static bool Accept(CharacterExtras extras) => ExtrasValidator.TryAccept(extras, Q, out _);

    private static string Pet() => PetNicknamesData.Neutralize(Convert.ToBase64String(Encoding.Unicode.GetBytes(
        string.Join("\r\n", PetNicknamesData.Header, "Nom", "73", "123", "411^2^Sparky^null^null"))))!;

    private static string Moodles() => MoodlesSanitizer.Sanitize(
        Convert.ToBase64String(MoodlesCodec.Encode([])), MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32)))!;

    [Fact]
    public void Des_extras_vides_passent() => Assert.True(Accept(CharacterExtras.None));

    [Fact]
    public void Des_extras_valides_passent()
        => Assert.True(Accept(new CharacterExtras(
            "{\"Bones\":{}}", "{\"DefaultOffset\":0.1}", "{\"Title\":\"le Voyageur\"}", Moodles(), Pet())));

    [Fact]
    public void CustomizePlus_au_plafond_passe_et_au_dessus_non()
    {
        static string Padded(int length) => "{\"a\":\"" + new string('x', length - 8) + "\"}";

        Assert.True(Accept(CharacterExtras.None with { CustomizePlus = Padded(Q.MaxCustomizePlusChars) }));
        Assert.False(Accept(CharacterExtras.None with { CustomizePlus = Padded(Q.MaxCustomizePlusChars + 1) }));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("[]")]
    [InlineData("{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":1}}}}}}}}}")]
    public void Un_json_invalide_ou_trop_profond_est_refuse(string json)
        => Assert.False(Accept(CharacterExtras.None with { CustomizePlus = json }));

    [Fact]
    public void Des_talons_non_nettoyes_sont_refuses()
        => Assert.False(Accept(CharacterExtras.None with { Heels = "{\"DefaultOffset\":0.1,\"Tags\":{}}" }));

    [Theory]
    [InlineData("{\"Title\":\"trente-trois caractères, un de trop\"}")]
    [InlineData("{\"Title\":\"a\\u0007b\"}")]
    [InlineData("{\"Title\":42}")]
    public void Un_titre_trop_long_ou_avec_un_controle_est_refuse(string json)
        => Assert.False(Accept(CharacterExtras.None with { Honorific = json }));

    [Fact]
    public void Des_moodles_non_nettoyes_sont_refuses()
    {
        var raw = Convert.ToBase64String(MoodlesCodec.Encode(
            [new MoodleStatus(Guid.NewGuid(), 1, "t", "d", "", 0, 0, 0, 1, 0, Guid.Empty, 0, "Nom@Monde", "")]));

        Assert.False(Accept(CharacterExtras.None with { Moodles = raw }));
    }

    [Fact]
    public void Des_surnoms_non_neutralises_sont_refuses()
        => Assert.False(Accept(CharacterExtras.None with
        {
            PetNicknames = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                string.Join("\r\n", PetNicknamesData.Header, "Nom", "73", "123"))),
        }));

    [Fact]
    public void Un_seul_champ_fautif_fait_refuser_le_manifeste_entier()
    {
        var manifest = new CharacterManifest(CharacterManifest.CurrentVersion, [], "", null,
            new CharacterExtras("{\"Bones\":{}}", null, "{\"Title\":\"a\\u0007\"}", null, null));

        Assert.False(ManifestValidator.TryAccept(manifest, Q, out var why));
        Assert.Contains("Honorific", why!);
    }
}
