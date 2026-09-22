using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Rendezvous;

public class RendezvousWireTests
{
    private static byte[] Ticket(byte seed) => Enumerable.Repeat(seed, RendezvousTicket.SizeInBytes).ToArray();

    [Fact]
    public void Une_annonce_fait_l_aller_retour()
    {
        var original = new Announcement([Ticket(1), Ticket(2)], [9, 8, 7]);

        Assert.True(RendezvousWire.TryReadAnnounce(RendezvousWire.Announce(original), out var parsed, out var why), why);
        Assert.Equal(2, parsed!.Tickets.Count);
        Assert.Equal(Ticket(1), parsed.Tickets[0]);
        Assert.Equal(new byte[] { 9, 8, 7 }, parsed.SealedCandidates);
    }

    [Fact]
    public void Une_annonce_sans_jeton_est_refusee()
    {
        Assert.False(RendezvousWire.TryReadAnnounce([RendezvousKind.Announce, 0], out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Trop_de_jetons_est_refuse()
    {
        // Un client qui annoncerait mille jetons ferait du serveur un index
        // gratuit : le plafond est une mesure anti-abus, pas une élégance.
        var frame = new byte[2 + (200 * RendezvousTicket.SizeInBytes) + 2];
        frame[0] = RendezvousKind.Announce;
        frame[1] = 200;

        Assert.False(RendezvousWire.TryReadAnnounce(frame, out _, out var why));
        Assert.Contains("jetons", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void Une_annonce_tronquee_est_refusee_sans_lever(int length)
    {
        Assert.False(RendezvousWire.TryReadAnnounce(new byte[length], out _, out _));
    }

    [Fact]
    public void Un_bloc_de_candidats_hors_bornes_est_refuse()
    {
        var frame = new byte[2 + RendezvousTicket.SizeInBytes + 2];
        frame[0] = RendezvousKind.Announce;
        frame[1] = 1;
        frame[^2] = 0xFF;
        frame[^1] = 0xFF;

        Assert.False(RendezvousWire.TryReadAnnounce(frame, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Le_prefixe_de_longueur_delimite_la_trame()
    {
        var framed = RendezvousWire.Frame([1, 2, 3]);

        Assert.Equal(7, framed.Length);
        Assert.Equal(3, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(framed));
    }

    [Fact]
    public void Un_annuaire_fait_l_aller_retour()
    {
        var entries = new List<DirectoryEntry>
        {
            new("rdv.ami.ch", "Chez l'amie"),
            new("rdv.exemple.ch:443", "Service commun"),
        };

        Assert.True(RendezvousWire.TryReadDirectory(RendezvousWire.Directory(entries), out var back, out var why), why);
        Assert.Equal(2, back.Count);
        Assert.Equal("rdv.ami.ch", back[0].Address);
        Assert.Equal("Service commun", back[1].Label);
    }

    [Fact]
    public void Un_annuaire_vide_est_lisible()
    {
        // Un service sans pair déclaré doit pouvoir répondre, sinon le client
        // ne distingue pas « personne » de « service en panne ».
        Assert.True(RendezvousWire.TryReadDirectory(RendezvousWire.Directory([]), out var back, out _));
        Assert.Empty(back);
    }

    [Fact]
    public void Un_annuaire_trop_long_est_refuse_a_l_ecriture()
    {
        var entries = Enumerable.Range(0, RendezvousWire.MaxDirectoryEntries + 1)
            .Select(i => new DirectoryEntry($"rdv{i}.exemple.ch", "x"))
            .ToList();

        Assert.Throws<ArgumentOutOfRangeException>(() => RendezvousWire.Directory(entries));
    }

    [Fact]
    public void Un_libelle_demesure_est_refuse_a_la_lecture()
    {
        // Le libellé vient du réseau et finit à l'écran : la borne est ici.
        var frame = new byte[] { RendezvousKind.DirectoryList, 1, 3 }
            .Concat("abc"u8.ToArray())
            .Concat(new byte[] { 200 })
            .Concat(Enumerable.Repeat((byte)0x41, 200))
            .ToArray();

        Assert.False(RendezvousWire.TryReadDirectory(frame, out _, out var why));
        Assert.Contains("libellé", why);
    }

    [Fact]
    public void Un_annuaire_tronque_est_refuse()
    {
        var frame = new byte[] { RendezvousKind.DirectoryList, 2, 3 }.Concat("abc"u8.ToArray()).ToArray();

        Assert.False(RendezvousWire.TryReadDirectory(frame, out _, out var why));
        Assert.Contains("tronqué", why);
    }

    [Fact]
    public void Une_candidature_fait_l_aller_retour()
    {
        var frame = RendezvousWire.DirectorySubmit("rdv.nouveau.ch:47900", "Chez le nouveau");

        Assert.True(RendezvousWire.TryReadDirectory(frame, out var back, out var why), why);
        var entry = Assert.Single(back);
        Assert.Equal("rdv.nouveau.ch:47900", entry.Address);
        Assert.Equal("Chez le nouveau", entry.Label);
    }

    [Fact]
    public void Un_libelle_vide_est_accepte()
    {
        // L'opérateur n'est pas obligé de nommer les services qu'il connaît.
        Assert.True(RendezvousWire.TryReadDirectory(
            RendezvousWire.Directory([new DirectoryEntry("rdv.ami.ch", "")]), out var back, out _));

        Assert.Equal("", Assert.Single(back).Label);
    }
}
