using System.Text;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

/// <summary>
/// Un échange fait jouer un fichier du jeu à la place d'un autre, sans fichier
/// à transférer. C'est la forme de bien des mods d'animation : une idle d'une
/// autre race. Vu en jeu : sans eux dans le manifeste, ces idles ne partaient
/// jamais.
/// </summary>
public class FileSwapTests
{
    private const string VieraIdle = "chara/human/c1801/animation/a0001/bt_common/resident/idle.pap";
    private const string HyurIdle = "chara/human/c0201/animation/a0001/bt_common/resident/idle.pap";
    private const string VieraPose = "chara/human/c1801/animation/a0001/bt_common/emote/pose01_loop.pap";
    private const string HyurPose = "chara/human/c0201/animation/a0001/bt_common/emote/pose01_loop.pap";
    private const string Top = "chara/equipment/e0001/model/c0101e0001_top.mdl";

    private static readonly ResolvedFile TopFile =
        new(Top, BlobHash.OfContent(Encoding.UTF8.GetBytes("torse")), 5);

    private static CharacterManifest WithSwaps(params FileSwap[] swaps)
        => new(CharacterManifest.CurrentVersion, [], string.Empty, null, Swaps: swaps);

    [Fact]
    public void Le_manifeste_construit_porte_les_echanges_tries()
    {
        var manifest = ManifestBuilder.Build(
            [TopFile], string.Empty, null, Quotas.Default,
            [new FileSwap(VieraPose, HyurPose), new FileSwap(VieraIdle, HyurIdle)]).Manifest;

        // Ordre ordinal des chemins de jeu : « emote » avant « resident ».
        Assert.Equal([new FileSwap(VieraPose, HyurPose), new FileSwap(VieraIdle, HyurIdle)], manifest.SwapsOrNone);
    }

    [Fact]
    public void L_ordre_d_arrivee_des_echanges_ne_change_pas_le_hash()
    {
        var a = ManifestBuilder.Build([], string.Empty, null, Quotas.Default,
            [new FileSwap(VieraIdle, HyurIdle), new FileSwap(VieraPose, HyurPose)]).Manifest;
        var b = ManifestBuilder.Build([], string.Empty, null, Quotas.Default,
            [new FileSwap(VieraPose, HyurPose), new FileSwap(VieraIdle, HyurIdle)]).Manifest;

        Assert.Equal(ManifestCodec.HashOf(a), ManifestCodec.HashOf(b));
    }

    [Fact]
    public void Un_manifeste_sans_echange_s_encode_comme_avant()
    {
        // Même encodage, donc même empreinte : un pair déjà à jour ne se voit
        // pas réannoncer une apparence identique à cause de cette évolution.
        var sans = new CharacterManifest(CharacterManifest.CurrentVersion, [], string.Empty, null);
        var vide = sans with { Swaps = [] };

        Assert.DoesNotContain("\"w\"", Encoding.UTF8.GetString(ManifestCodec.Encode(sans)));
        Assert.Equal(ManifestCodec.Encode(sans), ManifestCodec.Encode(vide));
    }

    [Fact]
    public void L_aller_retour_conserve_les_echanges()
    {
        var manifest = WithSwaps(new FileSwap(VieraIdle, HyurIdle));

        Assert.True(ManifestCodec.TryDecompress(ManifestCodec.Compress(manifest), Quotas.Default, out var back, out var why), why);
        Assert.Equal(manifest.SwapsOrNone, back!.SwapsOrNone);
    }

    [Fact]
    public void Un_echange_legitime_est_accepte()
        => Assert.True(ManifestValidator.TryAccept(WithSwaps(new FileSwap(VieraIdle, HyurIdle)), Quotas.Default, out var why), why);

    [Theory]
    [InlineData(VieraIdle, "chara/human/c0201/obj/body/b0001/model/c0201b0001_top.mdl")]
    [InlineData(VieraIdle, "../../../etc/passwd.pap")]
    [InlineData(VieraIdle, "Chara/Human/C0201/animation/a0001/bt_common/resident/idle.pap")]
    [InlineData("shader/sm5/shpk/skin.shpk", "shader/sm5/shpk/autre.shpk")]
    public void Un_echange_douteux_fait_rejeter_le_manifeste_entier(string gamePath, string target)
        => Assert.False(ManifestValidator.TryAccept(WithSwaps(new FileSwap(gamePath, target)), Quotas.Default, out _));

    [Fact]
    public void Deux_echanges_pour_un_meme_chemin_sont_rejetes()
        => Assert.False(ManifestValidator.TryAccept(
            WithSwaps(new FileSwap(VieraIdle, HyurIdle), new FileSwap(VieraIdle, VieraPose)), Quotas.Default, out _));

    [Fact]
    public void Les_echanges_comptent_dans_le_plafond_de_chemins()
    {
        var quotas = Quotas.Default with { MaxGamePaths = 1 };

        Assert.False(ManifestValidator.TryAccept(
            WithSwaps(new FileSwap(VieraIdle, HyurIdle), new FileSwap(VieraPose, HyurPose)), quotas, out _));
    }

    [Fact]
    public void Le_plan_pose_l_echange_vers_le_chemin_de_jeu_cible()
    {
        Assert.True(AppearancePlanner.TryBuild(
            WithSwaps(new FileSwap(VieraIdle, HyurIdle)), new FakeBlobStore(), Quotas.Default, out var plan, out var why), why);

        Assert.Equal(HyurIdle, plan!.PathMap[VieraIdle]);
    }

    [Fact]
    public void Un_echange_qui_contredit_un_fichier_est_rejete()
    {
        var store = new FakeBlobStore();
        var hash = store.AddContent(Encoding.UTF8.GetBytes("torse"));
        var manifest = new CharacterManifest(
            CharacterManifest.CurrentVersion, [new FileReplacement([Top], hash, 5)], string.Empty, null,
            Swaps: [new FileSwap(Top, "chara/equipment/e0002/model/c0101e0002_top.mdl")]);

        Assert.False(AppearancePlanner.TryBuild(manifest, store, Quotas.Default, out _, out _));
    }

    [Fact]
    public void Bloquer_les_animations_retire_aussi_leurs_echanges()
    {
        var manifest = WithSwaps(new FileSwap(VieraIdle, HyurIdle));

        var filtered = TransientPolicy.Filter(manifest, TransientCategories.All with { Animations = false });

        Assert.Empty(filtered.SwapsOrNone);
    }
}
