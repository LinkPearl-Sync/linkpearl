using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

public class ExtrasDiffTests
{
    private static CharacterManifest M(string meta, CharacterExtras? extras)
        => new(CharacterManifest.CurrentVersion,
               [new FileReplacement(["chara/x.mdl"], BlobHash.OfContent("x"u8), 1)], meta, null, extras);

    [Fact]
    public void Un_titre_seul_change_ne_demande_que_les_extras()
    {
        var before = M("", new CharacterExtras(null, null, "{\"Title\":\"a\"}", null, null));
        var after = M("", new CharacterExtras(null, null, "{\"Title\":\"b\"}", null, null));

        Assert.True(ExtrasDiff.OnlyExtrasDiffer(before, after));
        Assert.Equal(new ExtrasChange(false, false, true, false, false),
                     ExtrasDiff.Between(before.ExtrasOrNone, after.ExtrasOrNone));
    }

    [Fact]
    public void Des_fichiers_changes_demandent_une_application_complete()
        => Assert.False(ExtrasDiff.OnlyExtrasDiffer(M("", null), M("AAAA", null)));

    [Fact]
    public void Un_extra_qui_disparait_est_un_changement()
        => Assert.True(ExtrasDiff.Between(
            new CharacterExtras("{}", null, null, null, null), CharacterExtras.None).CustomizePlus);

    [Fact]
    public void Rien_de_change_ne_change_rien()
        => Assert.False(ExtrasDiff.Between(CharacterExtras.None, CharacterExtras.None).Any);
}
