using Linkpearl.Core.Manifest;
using Linkpearl.Core.Tests.Sync;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class TransientMemoryTests
{
    private const string Idle = "chara/human/c0101/animation/a0001/bt_common/resident/idle.pap";
    private const string Sit = "chara/human/c0101/animation/a0001/bt_common/emote/sit_loop.pap";
    private const uint Paladin = 19;
    private const uint Bard = 23;

    private readonly MovableClock _clock = new();

    [Fact]
    public void Un_chemin_vu_sous_un_job_n_est_rendu_que_pour_ce_job()
    {
        var memory = new TransientMemory(_clock);

        Assert.True(memory.Record(Idle, Paladin));

        Assert.Contains(Idle, memory.PathsFor(Paladin));
        Assert.DoesNotContain(Idle, memory.PathsFor(Bard));
    }

    [Fact]
    public void Revu_sous_un_autre_job_il_devient_commun()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(Idle, Paladin);

        Assert.True(memory.Record(Idle, Bard));

        Assert.Contains(Idle, memory.PathsFor(Paladin));
        Assert.Contains(Idle, memory.PathsFor(Bard));
        Assert.Contains(Idle, memory.PathsFor(40));
    }

    [Fact]
    public void Revu_sous_le_meme_job_ce_n_est_pas_nouveau()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(Idle, Paladin);

        Assert.False(memory.Record(Idle, Paladin));
    }

    [Fact]
    public void Les_chemins_sont_normalises()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(@"CHARA\human\c0101\animation\a0001\bt_common\resident\IDLE.pap", Paladin);

        Assert.Contains(Idle, memory.PathsFor(Paladin));
        Assert.False(memory.Record(Idle, Paladin));
    }

    [Fact]
    public void Oublier_efface_partout()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(Idle, Paladin);
        memory.Record(Idle, Bard);

        memory.Forget(Idle);

        Assert.Empty(memory.PathsFor(Paladin));
        Assert.Empty(memory.PathsFor(Bard));
    }

    [Fact]
    public void Ce_qui_n_a_pas_ete_revu_depuis_trente_jours_est_purge()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(Idle, Paladin);
        _clock.Advance(TimeSpan.FromDays(20));
        memory.Record(Sit, Paladin);
        _clock.Advance(TimeSpan.FromDays(15));

        Assert.Equal(1, memory.Purge(TimeSpan.FromDays(30)));

        Assert.Equal([Sit], memory.PathsFor(Paladin));
    }

    [Fact]
    public void Le_json_fait_l_aller_retour()
    {
        var memory = new TransientMemory(_clock);
        memory.Record(Idle, Paladin);
        memory.Record(Idle, Bard);
        memory.Record(Sit, Paladin);

        var back = TransientMemory.FromJson(memory.ToJson(), _clock);

        Assert.Equal(memory.PathsFor(Paladin).Order(), back.PathsFor(Paladin).Order());
        Assert.Equal([Idle], back.PathsFor(Bard));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas du json")]
    [InlineData("{\"v\":1,\"e\":[{\"p\":42}]}")]
    [InlineData("[1,2,3]")]
    public void Un_json_illisible_donne_une_memoire_vide(string json)
        => Assert.Empty(TransientMemory.FromJson(json, _clock).PathsFor(Paladin));

    [Fact]
    public void Au_plafond_un_nouveau_chemin_n_est_pas_retenu()
    {
        var memory = new TransientMemory(_clock);

        for (var i = 0; i < TransientMemory.MaxPaths; i++)
            memory.Record($"chara/action/a{i}.tmb", Paladin);

        Assert.False(memory.Record(Idle, Paladin));
        Assert.Equal(TransientMemory.MaxPaths, memory.PathsFor(Paladin).Count);
    }
}
