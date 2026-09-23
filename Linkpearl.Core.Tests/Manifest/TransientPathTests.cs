using Linkpearl.Core.Manifest;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class TransientPathTests
{
    private const string Idle = "chara/human/c0101/animation/a0001/bt_common/resident/idle.pap";

    [Fact]
    public void Le_prefixe_de_collection_est_retire()
        => Assert.Equal(@"C:\Mods\Assise\idle.pap", TransientPath.WithoutPenumbraPrefix(@"|1_2_3|C:\Mods\Assise\idle.pap"));

    [Fact]
    public void Sans_prefixe_le_chemin_est_rendu_tel_quel()
        => Assert.Equal(@"C:\Mods\idle.pap", TransientPath.WithoutPenumbraPrefix(@"C:\Mods\idle.pap"));

    [Fact]
    public void Un_fichier_du_disque_est_modde()
    {
        Assert.True(TransientPath.TryModded(Idle, @"|1_2|C:\Mods\Assise\idle.pap", out var actual));
        Assert.Equal(@"C:\Mods\Assise\idle.pap", actual);
    }

    [Fact]
    public void Un_echange_vers_un_autre_chemin_de_jeu_est_modde()
    {
        const string Other = "chara/human/c0101/animation/a0001/bt_common/emote/sit.pap";

        Assert.True(TransientPath.TryModded(Idle, "|1_2|" + Other, out var actual));
        Assert.Equal(Other, actual);
    }

    [Theory]
    [InlineData(Idle)]
    [InlineData("|1_2|" + Idle)]
    [InlineData(@"CHARA\human\c0101\animation\a0001\bt_common\resident\IDLE.pap")]
    [InlineData("")]
    public void Le_chemin_lui_meme_ou_rien_n_est_pas_modde(string resolved)
        => Assert.False(TransientPath.TryModded(Idle, resolved, out _));
}
