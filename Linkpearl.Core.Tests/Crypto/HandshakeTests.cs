using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

/// <summary>Horloge figée, pour que l'anti-rejeu soit testable.</summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public class HandshakeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class Pair : IDisposable
    {
        public ECDsa AliceIdentity { get; } = CryptoPrimitives.GenerateIdentity();
        public ECDsa BobIdentity { get; } = CryptoPrimitives.GenerateIdentity();
        public FixedClock Clock { get; } = new(T0);

        public byte[] AlicePublic => CryptoPrimitives.ExportPublicPoint(AliceIdentity);
        public byte[] BobPublic => CryptoPrimitives.ExportPublicPoint(BobIdentity);

        public HandshakeInitiator Alice() => new(AliceIdentity, Clock);
        public HandshakeResponder Bob() => new(BobIdentity, Clock);

        public void Dispose()
        {
            AliceIdentity.Dispose();
            BobIdentity.Dispose();
        }
    }

    private static Func<byte[], bool> Accepts(byte[] expected)
        => candidate => candidate.AsSpan().SequenceEqual(expected);

    private static readonly Func<byte[], bool> RefusesEveryone = _ => false;

    [Fact]
    public void Un_echange_nominal_donne_des_cles_accordees()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        Assert.True(bob.TryHandleMessage1(msg1, out var msg2, out var why1), why1);
        Assert.True(alice.TryHandleMessage2(msg2!, Accepts(pair.BobPublic), out var msg3, out var aliceKeys, out var why2), why2);
        Assert.True(bob.TryHandleMessage3(msg3!, Accepts(pair.AlicePublic), out var bobKeys, out var why3), why3);

        // Ce qu'Alice émet, Bob le reçoit, et réciproquement.
        Assert.Equal(aliceKeys!.SendKey, bobKeys!.ReceiveKey);
        Assert.Equal(aliceKeys.ReceiveKey, bobKeys.SendKey);
        Assert.Equal(aliceKeys.SessionId, bobKeys.SessionId);

        // Les deux sens ne partagent jamais la même clé.
        Assert.NotEqual(aliceKeys.SendKey, aliceKeys.ReceiveKey);

        // Chacun repart avec l'identité prouvée de l'autre.
        Assert.Equal(pair.BobPublic, aliceKeys.PeerPublicKey);
        Assert.Equal(pair.AlicePublic, bobKeys.PeerPublicKey);
    }

    [Fact]
    public void Deux_echanges_successifs_ne_donnent_jamais_les_memes_cles()
    {
        // Confidentialité persistante : les clés viennent d'éphémères, pas des
        // identités. Compromettre une identité ne déchiffre pas le passé.
        using var pair = new Pair();

        var first = Complete(pair);
        var second = Complete(pair);

        Assert.NotEqual(first.SendKey, second.SendKey);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    [Fact]
    public void Un_pair_inconnu_du_carnet_est_refuse()
    {
        // L'autorisation vient du carnet local, jamais du réseau. C'est ce qui
        // rend un rendez-vous malveillant incapable d'usurper une identité.
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        Assert.True(bob.TryHandleMessage1(msg1, out var msg2, out _));
        Assert.False(alice.TryHandleMessage2(msg2!, RefusesEveryone, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_identite_substituee_par_le_rendez_vous_est_refusee()
    {
        using var pair = new Pair();
        using var imposteur = CryptoPrimitives.GenerateIdentity();

        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        Assert.True(bob.TryHandleMessage1(msg1, out var msg2, out _));

        // Alice n'attend que la clé de l'imposteur : le vrai Bob doit échouer.
        Assert.False(alice.TryHandleMessage2(
            msg2!, Accepts(CryptoPrimitives.ExportPublicPoint(imposteur)), out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_message_2_altere_est_refuse()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        Assert.True(bob.TryHandleMessage1(msg1, out var msg2, out _));
        msg2![^5] ^= 0x01;

        Assert.False(alice.TryHandleMessage2(msg2, Accepts(pair.BobPublic), out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_message_3_altere_est_refuse()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        bob.TryHandleMessage1(msg1, out var msg2, out _);
        alice.TryHandleMessage2(msg2!, Accepts(pair.BobPublic), out var msg3, out _, out _);
        msg3![^5] ^= 0x01;

        Assert.False(bob.TryHandleMessage3(msg3, Accepts(pair.AlicePublic), out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_ephemere_substitue_casse_la_liaison_entre_identite_et_secret()
    {
        // Attaque par relais d'identité : on remplace l'éphémère d'Alice par le
        // sien. Le transcript change, donc la signature de Bob ne correspond
        // plus à ce qu'Alice a vu.
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        var falsifie = msg1.ToArray();
        using var ephemereAdverse = CryptoPrimitives.GenerateEphemeral();
        CryptoPrimitives.ExportPublicPoint(ephemereAdverse).CopyTo(falsifie.AsSpan(HandshakeFormat.EphemeralOffsetInMessage1));

        Assert.True(bob.TryHandleMessage1(falsifie, out var msg2, out _));
        Assert.False(alice.TryHandleMessage2(msg2!, Accepts(pair.BobPublic), out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_message_1_trop_ancien_est_refuse()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var msg1 = alice.CreateMessage1();

        pair.Clock.UtcNow = T0.AddMinutes(5);

        var bob = pair.Bob();
        Assert.False(bob.TryHandleMessage1(msg1, out _, out var why));
        Assert.Contains("horodatage", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_message_1_venu_du_futur_est_refuse()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var msg1 = alice.CreateMessage1();

        pair.Clock.UtcNow = T0.AddMinutes(-5);

        var bob = pair.Bob();
        Assert.False(bob.TryHandleMessage1(msg1, out _, out var why));
        Assert.Contains("horodatage", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_version_majeure_inconnue_est_refusee()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var msg1 = alice.CreateMessage1();
        msg1[0] = 0xFF;   // version majeure

        var bob = pair.Bob();
        Assert.False(bob.TryHandleMessage1(msg1, out _, out var why));
        Assert.Contains("version", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(200)]
    public void Un_message_tronque_est_refuse_sans_lever(int length)
    {
        using var pair = new Pair();
        var bob = pair.Bob();

        Assert.False(bob.TryHandleMessage1(new byte[length], out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_point_ephemere_hors_courbe_est_refuse_sans_lever()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var msg1 = alice.CreateMessage1();

        // Un point arbitraire : le runtime ne le refuse pas, notre vérification si.
        msg1.AsSpan(HandshakeFormat.EphemeralOffsetInMessage1 + 1, 32).Fill(0x01);

        var bob = pair.Bob();
        Assert.False(bob.TryHandleMessage1(msg1, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void La_chaine_d_authentification_courte_est_identique_des_deux_cotes()
    {
        using var pair = new Pair();
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        bob.TryHandleMessage1(msg1, out var msg2, out _);
        alice.TryHandleMessage2(msg2!, Accepts(pair.BobPublic), out var msg3, out var aliceKeys, out _);
        bob.TryHandleMessage3(msg3!, Accepts(pair.AlicePublic), out var bobKeys, out _);

        var fromAlice = ShortAuthString.Of(aliceKeys!.SessionId);
        var fromBob = ShortAuthString.Of(bobKeys!.SessionId);

        Assert.Equal(fromAlice, fromBob);
        Assert.Equal(6, fromAlice.Count);
        Assert.All(fromAlice, word => Assert.False(string.IsNullOrWhiteSpace(word)));
    }

    private static SessionKeys Complete(Pair pair)
    {
        var alice = pair.Alice();
        var bob = pair.Bob();

        var msg1 = alice.CreateMessage1();
        bob.TryHandleMessage1(msg1, out var msg2, out _);
        alice.TryHandleMessage2(msg2!, Accepts(pair.BobPublic), out var msg3, out var keys, out _);
        bob.TryHandleMessage3(msg3!, Accepts(pair.AlicePublic), out _, out _);

        return keys!;
    }
}
