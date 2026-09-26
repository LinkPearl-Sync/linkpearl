using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Sync;

/// <summary>La liste gardée par le client, et qui a le droit d'y passer.</summary>
public sealed class OpenCircleTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly MovableClock _clock = new();

    public void Dispose() => _key.Dispose();

    private OpenCircle Circle() => new([ServiceConsensus.PublicPoint(_key)], _clock);

    private byte[] Document(uint version = 1)
    {
        var issued = _clock.UtcNow.ToUnixTimeSeconds();
        return ServiceConsensus.Sign(new ServiceConsensus(
            version, issued, issued + (long)ServiceConsensus.Lifetime.TotalSeconds,
            [
                new ConsensusEntry("rdv.a.ch:47900", "A", [.. Enumerable.Repeat((byte)1, 8)]),
                new ConsensusEntry("rdv.b.ch:47900", "B", [.. Enumerable.Repeat((byte)2, 8)]),
                new ConsensusEntry("rdv.c.ch:47900", "C", [.. Enumerable.Repeat((byte)3, 8)]),
            ]), _key);
    }

    private static PairRecord Pair(GroupOrigin? group = null) => new()
    {
        Id = PeerId.Of(new byte[65]),
        PairSecret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)],
        DisplayName = "Pair",
        Rendezvous = [new RendezvousAddress("rdv.ancre.ch", 47900)],
        Trust = PairTrust.Accepted,
        PairedAt = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero),
        Group = group,
    };

    private static GroupOrigin Origin(bool pinned, bool isPublic)
        => new(GroupId.Of(new byte[32]), PlayerFingerprint.Of("alice", 21), PlayerFingerprint.Of("bob", 21))
        {
            Pinned = pinned,
            Public = isPublic,
        };

    [Fact]
    public void Sans_liste_aucun_service_ouvert()
        => Assert.Empty(Circle().PlacesFor(Pair()));

    [Fact]
    public void Une_paire_du_carnet_recoit_deux_services()
    {
        var circle = Circle();
        Assert.True(circle.Offer(Document(), out var why), why);

        Assert.Equal(ServicePlacement.Chosen, circle.PlacesFor(Pair()).Count);
    }

    [Fact]
    public void Une_liste_expiree_rend_la_main_a_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        _clock.Advance(ServiceConsensus.Lifetime);

        Assert.Null(circle.Current);
        Assert.Empty(circle.PlacesFor(Pair()));
    }

    [Fact]
    public void Une_version_anterieure_est_refusee_et_la_precedente_reste()
    {
        var circle = Circle();
        circle.Offer(Document(version: 5), out _);

        Assert.False(circle.Offer(Document(version: 4), out var why));
        Assert.Contains("antérieure", why);
        Assert.Equal(5u, circle.Current!.Version);
    }

    [Fact]
    public void Une_liste_expiree_ne_bloque_plus_une_version_inferieure()
    {
        // Une autorité dont le registre a été remis à zéro repart plus bas :
        // tant que la liste détenue vaut encore, la règle de version protège ;
        // une fois expirée, elle ne protège plus rien et ne doit rien bloquer.
        var circle = Circle();
        circle.Offer(Document(version: 5), out _);

        _clock.Advance(ServiceConsensus.Lifetime);

        Assert.True(circle.Offer(Document(version: 1), out var why), why);
        Assert.Equal(1u, circle.Current!.Version);
    }

    [Fact]
    public void Une_liste_alteree_est_refusee_et_la_precedente_reste()
    {
        var circle = Circle();
        circle.Offer(Document(version: 1), out _);
        var altered = Document(version: 2);
        altered[30] ^= 1;

        Assert.False(circle.Offer(altered, out _));
        Assert.Equal(1u, circle.Current!.Version);
    }

    [Fact]
    public void Le_cercle_desactive_ne_donne_rien()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);
        circle.Enabled = false;

        Assert.Empty(circle.PlacesFor(Pair()));
    }

    [Fact]
    public void Un_membre_de_groupe_non_epingle_reste_dans_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Empty(circle.PlacesFor(Pair(Origin(pinned: false, isPublic: false))));
    }

    [Fact]
    public void Un_membre_du_groupe_Public_reste_dans_l_ancrage()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Empty(circle.PlacesFor(Pair(Origin(pinned: true, isPublic: true))));
    }

    [Fact]
    public void Un_membre_epingle_d_un_groupe_prive_passe_par_le_cercle_ouvert()
    {
        var circle = Circle();
        circle.Offer(Document(), out _);

        Assert.Equal(ServicePlacement.Chosen, circle.PlacesFor(Pair(Origin(pinned: true, isPublic: false))).Count);
    }
}
