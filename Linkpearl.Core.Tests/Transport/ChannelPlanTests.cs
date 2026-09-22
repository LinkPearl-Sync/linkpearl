using Linkpearl.Core.Transport;
using Xunit;

namespace Linkpearl.Core.Tests.Transport;

/// <summary>
/// Le débit du projet dépend entièrement du multi-canal : un canal unique
/// plafonne à 0,41 Mo/s à 60 ms de latence, mesuré. Ce répartiteur est donc une
/// pièce centrale, pas un détail d'optimisation.
/// </summary>
public class ChannelPlanTests
{
    [Fact]
    public void Le_canal_zero_est_reserve_au_controle()
    {
        // Le plan de contrôle ne doit jamais attendre derrière des mégaoctets de
        // texture : un message de présence ou une annulation doit passer tout de
        // suite.
        var plan = new ChannelPlan(dataChannels: 4);

        for (var i = 0; i < 50; i++)
            Assert.NotEqual(0, plan.Next(1024));
    }

    [Fact]
    public void Les_canaux_de_donnees_sont_tous_utilises()
    {
        var plan = new ChannelPlan(dataChannels: 4);
        var used = new HashSet<byte>();

        for (var i = 0; i < 40; i++)
            used.Add(plan.Next(1024));

        Assert.Equal(4, used.Count);
    }

    [Fact]
    public void La_charge_va_au_canal_le_moins_charge()
    {
        var plan = new ChannelPlan(dataChannels: 3);

        var premier = plan.Next(1_000_000);
        var deuxieme = plan.Next(10);
        var troisieme = plan.Next(10);

        // Le canal chargé d'un mégaoctet ne doit pas recevoir les suivants.
        Assert.NotEqual(premier, deuxieme);
        Assert.NotEqual(premier, troisieme);
    }

    [Fact]
    public void Un_canal_libere_redevient_candidat()
    {
        var plan = new ChannelPlan(dataChannels: 2);

        var charge = plan.Next(1_000_000);
        plan.Completed(charge, 1_000_000);

        var ensuite = new HashSet<byte>();
        for (var i = 0; i < 10; i++)
            ensuite.Add(plan.Next(10));

        Assert.Contains(charge, ensuite);
    }

    [Fact]
    public void La_repartition_reste_equilibree_sur_des_blocs_de_meme_taille()
    {
        var plan = new ChannelPlan(dataChannels: 8);
        var counts = new Dictionary<byte, int>();

        for (var i = 0; i < 800; i++)
        {
            var channel = plan.Next(16 * 1024);
            counts[channel] = counts.GetValueOrDefault(channel) + 1;
        }

        Assert.All(counts.Values, count => Assert.Equal(100, count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void Un_nombre_de_canaux_hors_bornes_est_refuse(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChannelPlan(channels));
    }
}
