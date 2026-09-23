using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public class TransientCategoriesTests
{
    [Theory]
    [InlineData("chara/human/c0101/animation/a0001/bt_common/emote/pose01_loop.pap")]
    [InlineData("chara/action/emote/pose01.tmb")]
    public void Pap_et_tmb_sont_des_animations(string path)
    {
        Assert.False((TransientCategories.All with { Animations = false }).Allows(path));
        Assert.True((TransientCategories.All with { Vfx = false, Sounds = false }).Allows(path));
    }

    [Theory]
    [InlineData("vfx/common/eff/cmmn_aura01.avfx")]
    [InlineData("vfx/common/texture/aura.atex")]
    public void Avfx_et_atex_sont_des_vfx(string path)
    {
        Assert.False((TransientCategories.All with { Vfx = false }).Allows(path));
        Assert.True((TransientCategories.All with { Animations = false, Sounds = false }).Allows(path));
    }

    [Fact]
    public void Scd_est_un_son()
    {
        Assert.False((TransientCategories.All with { Sounds = false }).Allows("sound/voice/vo_emote/a.scd"));
        Assert.True((TransientCategories.All with { Animations = false, Vfx = false }).Allows("sound/voice/vo_emote/a.scd"));
    }

    [Theory]
    [InlineData("chara/equipment/e0001/model/c0101e0001_top.mdl")]
    [InlineData("chara/equipment/e0001/texture/v01_c0101e0001_top_n.tex")]
    public void Un_fichier_statique_est_toujours_permis(string path)
        => Assert.True(TransientCategories.None.Allows(path));

    [Fact]
    public void None_bloque_les_trois()
    {
        Assert.False(TransientCategories.None.Allows("a.pap"));
        Assert.False(TransientCategories.None.Allows("a.avfx"));
        Assert.False(TransientCategories.None.Allows("a.scd"));
    }

    [Fact]
    public void And_ne_permet_que_ce_que_les_deux_permettent()
    {
        var global = TransientCategories.All with { Sounds = false };
        var pair = TransientCategories.All with { Animations = false };

        Assert.Equal(new TransientCategories(false, true, false), global.And(pair));
    }

    [Theory]
    [InlineData("a.pap", true)]
    [InlineData("a.TMB", true)]
    [InlineData("a.avfx", true)]
    [InlineData("a.atex", true)]
    [InlineData("a.scd", true)]
    [InlineData("a.mdl", false)]
    [InlineData("dossier.pap/a.tex", false)]
    public void IsTransient_regarde_la_derniere_extension(string path, bool expected)
        => Assert.Equal(expected, TransientCategories.IsTransient(path));
}
