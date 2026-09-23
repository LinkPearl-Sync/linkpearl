using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

public class TransientPolicyTests
{
    private const string Top = "chara/equipment/e0001/model/c0101e0001_top.mdl";
    private const string Idle = "chara/human/c0101/animation/a0001/bt_common/resident/idle.pap";
    private const string Timeline = "chara/action/emote/pose01.tmb";
    private const string Aura = "vfx/common/eff/cmmn_aura01.avfx";
    private const string Voice = "sound/voice/vo_emote/a.scd";

    private static FileReplacement File(params string[] paths)
        => new(paths, BlobHash.OfContent(System.Text.Encoding.UTF8.GetBytes(string.Join("|", paths))), 1);

    private static CharacterManifest Manifest(params FileReplacement[] replacements)
        => new(CharacterManifest.CurrentVersion, replacements, string.Empty, null);

    private static IEnumerable<string> Paths(CharacterManifest manifest)
        => manifest.Replacements.SelectMany(r => r.GamePaths);

    [Fact]
    public void Tout_accepte_ne_retire_rien_et_rend_la_meme_instance()
    {
        var manifest = Manifest(File(Top), File(Idle), File(Aura), File(Voice));

        Assert.Same(manifest, TransientPolicy.Filter(manifest, TransientCategories.All));
    }

    [Fact]
    public void Bloquer_les_animations_retire_pap_et_tmb_seulement()
    {
        var manifest = Manifest(File(Top), File(Idle), File(Timeline), File(Aura), File(Voice));

        var filtered = TransientPolicy.Filter(manifest, TransientCategories.All with { Animations = false });

        Assert.Equal(new[] { Top, Aura, Voice }.Order(), Paths(filtered).Order());
    }

    [Fact]
    public void Une_entree_a_chemins_meles_garde_ses_chemins_permis()
    {
        var mixed = File(Top, Idle);

        var filtered = TransientPolicy.Filter(Manifest(mixed), TransientCategories.None);

        Assert.Equal([Top], Assert.Single(filtered.Replacements).GamePaths);
        Assert.Equal(mixed.Hash, filtered.Replacements[0].Hash);
    }

    [Fact]
    public void Une_entree_dont_tous_les_chemins_sont_bloques_disparait()
    {
        var filtered = TransientPolicy.Filter(Manifest(File(Top), File(Voice)), TransientCategories.All with { Sounds = false });

        Assert.Equal([Top], Paths(filtered));
    }

    [Fact]
    public void Le_reste_du_manifeste_est_conserve()
    {
        var manifest = Manifest(File(Top), File(Voice)) with
        {
            MetaManipulations = "AAAA",
            GlamourerState = "BBBB",
            Extras = CharacterExtras.None with { Honorific = "{}" },
        };

        var filtered = TransientPolicy.Filter(manifest, TransientCategories.None);

        Assert.Equal("AAAA", filtered.MetaManipulations);
        Assert.Equal("BBBB", filtered.GlamourerState);
        Assert.Equal("{}", filtered.ExtrasOrNone.Honorific);
    }
}
