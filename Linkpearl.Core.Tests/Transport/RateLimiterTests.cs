using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Transport;
using Xunit;

namespace Linkpearl.Core.Tests.Transport;

/// <summary>
/// Mesuré au jalon 2 : à vingt-quatre canaux, le ping observé montait à 145 ms
/// pour 60 ms de latence réelle, et jusqu'à 343 ms. LiteNetLib n'a aucun
/// contrôle de congestion. Ce limiteur est donc ce qui empêche le ping de FFXIV
/// de s'effondrer pendant qu'un pair télécharge nos textures.
/// </summary>
public class RateLimiterTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private static readonly RateLimiterSettings Settings = new()
    {
        InitialBytesPerSecond = 512 * 1024,
        IncreaseBytesPerSecond = 128 * 1024,
        IncreaseInterval = TimeSpan.FromSeconds(2),
        DecreaseFactor = 0.7,
        CeilingBytesPerSecond = 8 * 1024 * 1024,
        FloorBytesPerSecond = 64 * 1024,
    };

    [Fact]
    public void Le_debit_part_au_reglage_initial()
    {
        var limiter = new RateLimiter(new FakeClock(), Settings);
        Assert.Equal(512 * 1024, limiter.BytesPerSecond);
    }

    [Fact]
    public void Le_seau_se_vide_puis_se_remplit_avec_le_temps()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        // On vide tout ce qui est disponible.
        while (limiter.TryConsume(16 * 1024)) { }

        Assert.False(limiter.TryConsume(16 * 1024));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryConsume(16 * 1024));
    }

    [Fact]
    public void Le_debit_moyen_sur_dix_secondes_respecte_le_reglage()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings with { IncreaseBytesPerSecond = 0 });

        var consumed = 0L;
        for (var tick = 0; tick < 1000; tick++)
        {
            while (limiter.TryConsume(4 * 1024))
                consumed += 4 * 1024;

            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        // Dix secondes à 512 Kio/s, avec la tolérance d'un seau initial plein.
        var expected = 10L * 512 * 1024;
        Assert.InRange(consumed, expected * 0.9, expected * 1.3);
    }

    [Fact]
    public void Le_debit_monte_quand_la_ligne_est_saine()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        limiter.Observe(lossPercent: 0, pingMs: 60);
        clock.Advance(TimeSpan.FromSeconds(2));
        limiter.Observe(lossPercent: 0, pingMs: 60);

        Assert.Equal((512 + 128) * 1024, limiter.BytesPerSecond);
    }

    [Fact]
    public void Le_debit_ne_monte_pas_avant_l_intervalle()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        limiter.Observe(0, 60);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        limiter.Observe(0, 60);

        Assert.Equal(512 * 1024, limiter.BytesPerSecond);
    }

    [Fact]
    public void La_perte_fait_chuter_le_debit()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        limiter.Observe(lossPercent: 3, pingMs: 60);

        Assert.Equal((long)(512 * 1024 * 0.7), limiter.BytesPerSecond);
    }

    [Fact]
    public void Debraye_le_limiteur_laisse_tout_passer_sauf_en_pause()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings) { Bypassed = true };

        // Bien plus que le seau n'en contient : rien ne retient l'envoi.
        for (var i = 0; i < 1000; i++)
            Assert.True(limiter.TryConsume(16 * 1024));

        // La pause, elle, tient toujours : c'est elle qui protège un combat.
        limiter.IsPaused = true;
        Assert.False(limiter.TryConsume(1));
    }

    [Fact]
    public void Quelques_millisecondes_sur_un_lien_local_ne_sont_pas_une_congestion()
    {
        // Vu en jeu entre deux clients du même poste : un aller-retour de 2 ms
        // qui passe à 4 fait cent pour cent de plus. Sans marge absolue, le
        // débit tombait au plancher en dix secondes et y restait, et la
        // réception affichait zéro pendant de longues secondes.
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        for (var i = 0; i < 10; i++)
        {
            limiter.Observe(lossPercent: 0, pingMs: i % 2 == 0 ? 2 : 6);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.True(limiter.BytesPerSecond > 512 * 1024, $"débit tombé à {limiter.BytesPerSecond} o/s");
    }

    [Fact]
    public void Un_ping_qui_gonfle_fait_chuter_le_debit_meme_sans_perte()
    {
        // C'est le cas observé au jalon 2 : aucune perte rapportée, et un ping
        // qui double. On remplit les files, et c'est le ping du jeu qui trinque.
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        limiter.Observe(lossPercent: 0, pingMs: 60);
        limiter.Observe(lossPercent: 0, pingMs: 145);

        Assert.Equal((long)(512 * 1024 * 0.7), limiter.BytesPerSecond);
    }

    [Fact]
    public void Une_chute_repousse_la_prochaine_montee()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        limiter.Observe(0, 60);
        clock.Advance(TimeSpan.FromSeconds(1.9));
        limiter.Observe(lossPercent: 3, pingMs: 60);
        var apresChute = limiter.BytesPerSecond;

        clock.Advance(TimeSpan.FromMilliseconds(200));
        limiter.Observe(0, 60);

        Assert.Equal(apresChute, limiter.BytesPerSecond);
    }

    [Fact]
    public void Le_debit_ne_depasse_jamais_le_plafond()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        for (var i = 0; i < 500; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            limiter.Observe(0, 60);
        }

        Assert.Equal(8 * 1024 * 1024, limiter.BytesPerSecond);
    }

    [Fact]
    public void Le_debit_ne_descend_jamais_sous_le_plancher()
    {
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings);

        for (var i = 0; i < 100; i++)
            limiter.Observe(lossPercent: 10, pingMs: 60);

        Assert.Equal(64 * 1024, limiter.BytesPerSecond);
    }

    [Fact]
    public void En_pause_plus_rien_ne_passe()
    {
        // Pause en combat et en donjon : personne ne veut voir son jeu ramer
        // parce qu'un ami vient d'arriver à portée.
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings) { IsPaused = true };

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.False(limiter.TryConsume(1024));
    }

    [Fact]
    public void La_sortie_de_pause_ne_libere_pas_un_seau_accumule()
    {
        // Sinon la fin d'un combat déclencherait une rafale de plusieurs
        // mégaoctets, soit exactement ce que la pause cherchait à éviter.
        var clock = new FakeClock();
        var limiter = new RateLimiter(clock, Settings) { IsPaused = true };

        clock.Advance(TimeSpan.FromMinutes(5));
        limiter.IsPaused = false;

        var consumed = 0L;
        while (limiter.TryConsume(4 * 1024))
            consumed += 4 * 1024;

        Assert.InRange(consumed, 0, limiter.BytesPerSecond);
    }
}
