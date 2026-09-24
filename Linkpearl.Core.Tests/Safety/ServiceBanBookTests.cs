using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Tests.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

public sealed class ServiceBanBookTests
{
    private static readonly BanParameters Cheap = new(1000);
    private static readonly RendezvousAddress One = new("rdv.un.ch", 47900);
    private static readonly RendezvousAddress Two = new("rdv.deux.ch", 47900);
    private static readonly byte[] SaltOne = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] SaltTwo = Enumerable.Repeat((byte)2, 32).ToArray();
    private static readonly PlayerFingerprint Mallory = PlayerFingerprint.Of("mallory", 21);
    private static readonly PlayerFingerprint Alice = PlayerFingerprint.Of("alice", 21);

    private readonly MovableClock _clock = new();

    private static BanList Listing(byte[] salt, params (string Name, string Reason)[] banned)
        => new(salt, Cheap, [.. banned.Select(entry => new BanEntry(BanList.Derive(entry.Name, 21, salt, Cheap), entry.Reason, 0))]);

    /// <summary>Ce que l'adaptateur fait, avec le nom qu'il est seul à connaître.</summary>
    private static void DeriveAll(ServiceBanBook book, PlayerFingerprint player, string name)
    {
        foreach (var missing in book.Missing(player))
            book.Record(player, missing.Key, BanList.Derive(name, 21, missing.Salt, missing.Parameters));
    }

    [Fact]
    public void Sans_liste_tout_le_monde_est_libre()
        => Assert.Equal(BanVerdict.Clear, new ServiceBanBook(_clock).Status(Mallory).Verdict);

    [Fact]
    public void Une_liste_vide_ne_demande_aucune_derivation()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, new BanList(SaltOne, Cheap, []));

        Assert.Empty(book.Missing(Mallory));
        Assert.Equal(BanVerdict.Clear, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Avant_la_derivation_le_verdict_attend()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        Assert.Equal(BanVerdict.Pending, book.Status(Alice).Verdict);
    }

    [Fact]
    public void Apres_la_derivation_le_liste_est_vu_avec_son_service_et_son_motif()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        DeriveAll(book, Mallory, "mallory");
        DeriveAll(book, Alice, "alice");

        Assert.Equal(new ServiceBanStatus(BanVerdict.Listed, new ServiceBan(One, "triche")), book.Status(Mallory));
        Assert.Equal(BanVerdict.Clear, book.Status(Alice).Verdict);
    }

    [Fact]
    public void Un_seul_service_qui_liste_suffit()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("alice", "autre")));
        book.SetList(Two, Listing(SaltTwo, ("mallory", "triche")));

        DeriveAll(book, Mallory, "mallory");

        Assert.Equal(Two, book.Status(Mallory).Ban!.Service);
    }

    [Fact]
    public void Une_liste_mise_a_jour_reutilise_les_derivations()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("alice", "autre")));
        DeriveAll(book, Mallory, "mallory");

        book.SetList(One, Listing(SaltOne, ("alice", "autre"), ("mallory", "triche")));

        Assert.Empty(book.Missing(Mallory));
        Assert.Equal(BanVerdict.Listed, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Retirer_un_service_leve_son_bannissement()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));
        DeriveAll(book, Mallory, "mallory");

        book.Retain([Two]);

        Assert.Equal(BanVerdict.Clear, book.Status(Mallory).Verdict);
    }

    [Fact]
    public void Screen_derive_ce_qui_manque_et_rend_le_verdict()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        var status = book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters));

        Assert.Equal(BanVerdict.Listed, status.Verdict);
    }

    [Fact]
    public void Screen_respecte_son_budget_par_minute()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));

        for (var i = 0; i < ServiceBanBook.MaxOnDemandPerMinute; i++)
        {
            var name = $"forge{i}";
            Assert.NotEqual(BanVerdict.Pending, book.Screen(
                PlayerFingerprint.Of(name, 21), (salt, parameters) => BanList.Derive(name, 21, salt, parameters)).Verdict);
        }

        Assert.Equal(BanVerdict.Pending, book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters)).Verdict);

        _clock.Advance(TimeSpan.FromMinutes(1.1));

        Assert.Equal(BanVerdict.Listed, book.Screen(Mallory, (salt, parameters) => BanList.Derive("mallory", 21, salt, parameters)).Verdict);
    }

    [Fact]
    public void Screen_ne_compte_pas_ce_qui_est_deja_derive()
    {
        var book = new ServiceBanBook(_clock);
        book.SetList(One, Listing(SaltOne, ("mallory", "triche")));
        DeriveAll(book, Mallory, "mallory");

        for (var i = 0; i < ServiceBanBook.MaxOnDemandPerMinute * 2; i++)
            Assert.Equal(BanVerdict.Listed, book.Screen(Mallory, (_, _) => throw new InvalidOperationException("déjà dérivé")).Verdict);
    }
}
