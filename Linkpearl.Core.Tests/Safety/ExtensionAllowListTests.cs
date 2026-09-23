using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public class ExtensionAllowListTests
{
    [Theory]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl")]
    [InlineData("chara/equipment/e6111/material/v0001/mt_c0201e6111_top_a.mtrl")]
    [InlineData("chara/human/c0201/obj/face/f0002/texture/--c0201f0002_etc_d.tex")]
    [InlineData("chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb")]
    [InlineData("chara/human/c0101/skeleton/base/b0001/phy_c0101b0001.phyb")]
    [InlineData("chara/human/c0801/obj/body/b0001/b0001.imc")]
    public void Les_extensions_de_l_apparence_statique_sont_acceptees(string path)
    {
        Assert.True(ExtensionAllowList.IsAllowed(path, out _));
    }

    [Theory]
    [InlineData("chara/action/anim.pap")]      // animation
    [InlineData("chara/action/timeline.tmb")]  // declencheur d'animation
    [InlineData("vfx/common/eff/truc.avfx")]   // effet visuel
    [InlineData("vfx/common/eff/truc.atex")]   // texture d'effet visuel
    [InlineData("sound/voice/truc.scd")]       // audio
    public void Les_ressources_transitoires_sont_admises(string path)
    {
        // Admises depuis la capture des animations, VFX et sons : leur contenu
        // est vérifié à part (TransientFileCheck), et le receveur peut les
        // bloquer par catégorie.
        Assert.True(ExtensionAllowList.IsAllowed(path, out var why), why);
    }

    [Fact]
    public void Le_paquet_de_shader_est_refuse()
    {
        // .shpk est du bytecode consomme par le pilote graphique. Aucune
        // validation cote plugin ne rend cela sur.
        Assert.False(ExtensionAllowList.IsAllowed("shader/sm5/shpk/character.shpk", out var why));
        Assert.NotNull(why);
    }

    [Theory]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top")]     // sans extension
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.")]    // point final seul
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.exe")]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.dll")]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.lua")]
    public void Tout_le_reste_est_refuse(string path)
    {
        Assert.False(ExtensionAllowList.IsAllowed(path, out _));
    }

    [Fact]
    public void C_est_la_derniere_extension_qui_compte()
    {
        // Un nom double ne doit pas laisser passer la premiere extension.
        Assert.False(ExtensionAllowList.IsAllowed("chara/equipment/e0101/model/truc.tex.exe", out _));
        Assert.True(ExtensionAllowList.IsAllowed("chara/equipment/e0101/model/truc.exe.tex", out _));
    }

    [Fact]
    public void Un_point_dans_un_repertoire_ne_compte_pas_comme_extension()
    {
        Assert.False(ExtensionAllowList.IsAllowed("chara/e.0101/model/truc", out _));
    }
}
