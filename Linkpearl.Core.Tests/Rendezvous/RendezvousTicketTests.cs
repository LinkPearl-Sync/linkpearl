using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

/// <summary>
/// Le rendez-vous ne doit rien apprendre de durable. Les pairs s'y annoncent
/// sous un jeton qui change toutes les dix minutes, dérivé d'un secret que le
/// serveur ne connaît pas.
/// </summary>
public class RendezvousTicketTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static (PeerId Alice, PeerId Bob, byte[] Nonce) Keys()
    {
        using var alice = CryptoPrimitives.GenerateIdentity();
        using var bob = CryptoPrimitives.GenerateIdentity();

        return (PeerId.Of(CryptoPrimitives.ExportPublicPoint(alice)),
                PeerId.Of(CryptoPrimitives.ExportPublicPoint(bob)),
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(PairingCode.NonceLength));
    }

    [Fact]
    public void Les_deux_pairs_derivent_le_meme_secret_quel_que_soit_l_initiateur()
    {
        // Le secret ne doit pas dépendre de qui a invité l'autre, sans quoi les
        // deux s'annonceraient sous des jetons différents et ne se
        // trouveraient jamais.
        var (alice, bob, nonce) = Keys();

        Assert.Equal(
            PairSecret.Derive(nonce, alice, bob),
            PairSecret.Derive(nonce, bob, alice));
    }

    [Fact]
    public void Un_autre_alea_donne_un_autre_secret()
    {
        var (alice, bob, nonce) = Keys();
        var autre = System.Security.Cryptography.RandomNumberGenerator.GetBytes(PairingCode.NonceLength);

        Assert.NotEqual(PairSecret.Derive(nonce, alice, bob), PairSecret.Derive(autre, alice, bob));
    }

    [Fact]
    public void Une_autre_paire_donne_un_autre_secret()
    {
        var (alice, bob, nonce) = Keys();
        using var tiers = CryptoPrimitives.GenerateIdentity();

        Assert.NotEqual(
            PairSecret.Derive(nonce, alice, bob),
            PairSecret.Derive(nonce, alice, PeerId.Of(CryptoPrimitives.ExportPublicPoint(tiers))));
    }

    [Fact]
    public void Les_deux_pairs_annoncent_le_meme_jeton_au_meme_instant()
    {
        var (alice, bob, nonce) = Keys();
        var secret = PairSecret.Derive(nonce, alice, bob);
        var clock = new FixedClock(T0);

        var tickets = new RendezvousTicket(clock);

        Assert.Equal(tickets.Current(secret), tickets.Current(secret));
    }

    [Fact]
    public void Le_jeton_change_de_fenetre_en_fenetre()
    {
        var (alice, bob, nonce) = Keys();
        var secret = PairSecret.Derive(nonce, alice, bob);
        var clock = new FixedClock(T0);
        var tickets = new RendezvousTicket(clock);

        var premier = tickets.Current(secret);
        clock.UtcNow = T0.AddMinutes(10);

        Assert.NotEqual(premier, tickets.Current(secret));
    }

    [Fact]
    public void Le_jeton_ne_change_pas_a_l_interieur_d_une_fenetre()
    {
        var (alice, bob, nonce) = Keys();
        var secret = PairSecret.Derive(nonce, alice, bob);
        var clock = new FixedClock(T0);
        var tickets = new RendezvousTicket(clock);

        var premier = tickets.Current(secret);
        clock.UtcNow = T0.AddMinutes(9);

        Assert.Equal(premier, tickets.Current(secret));
    }

    [Fact]
    public void On_annonce_la_fenetre_courante_et_la_suivante()
    {
        // Sans quoi deux pairs de part et d'autre d'une bascule ne se verraient
        // pas, et l'un des deux attendrait dix minutes sans comprendre.
        var (alice, bob, nonce) = Keys();
        var secret = PairSecret.Derive(nonce, alice, bob);
        var clock = new FixedClock(T0);
        var tickets = new RendezvousTicket(clock);

        var announced = tickets.Announce(secret);
        Assert.Equal(2, announced.Count);

        clock.UtcNow = T0.AddMinutes(10);
        Assert.Contains(tickets.Current(secret), announced);
    }

    [Fact]
    public void Un_jeton_fait_seize_octets()
    {
        var (alice, bob, nonce) = Keys();
        var secret = PairSecret.Derive(nonce, alice, bob);

        Assert.Equal(RendezvousTicket.SizeInBytes, new RendezvousTicket(new FixedClock(T0)).Current(secret).Length);
    }

    [Fact]
    public void Deux_paires_differentes_ne_se_croisent_jamais_sur_un_meme_jeton()
    {
        var (alice, bob, nonce) = Keys();
        var (carol, dave, autreNonce) = Keys();

        var tickets = new RendezvousTicket(new FixedClock(T0));

        Assert.NotEqual(
            tickets.Current(PairSecret.Derive(nonce, alice, bob)),
            tickets.Current(PairSecret.Derive(autreNonce, carol, dave)));
    }
}
