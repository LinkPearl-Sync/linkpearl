using System.Security.Cryptography;
using Linkpearl.Core.Crypto;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

public class SecureChannelTests
{
    private static (SecureChannel A, SecureChannel B) Pair()
    {
        var keyA = RandomNumberGenerator.GetBytes(32);
        var keyB = RandomNumberGenerator.GetBytes(32);
        var sessionId = RandomNumberGenerator.GetBytes(16);

        return (new SecureChannel(keyA, keyB, sessionId), new SecureChannel(keyB, keyA, sessionId));
    }

    [Fact]
    public void Un_message_fait_l_aller_retour()
    {
        var (a, b) = Pair();
        var frame = a.Seal(channel: 3, kind: 0x07, "bonjour"u8);

        Assert.True(b.TryOpen(frame, out var kind, out var channel, out var payload, out var why), why);
        Assert.Equal(0x07, kind);
        Assert.Equal(3, channel);
        Assert.Equal("bonjour"u8.ToArray(), payload);
    }

    [Fact]
    public void Les_nonces_ne_se_repetent_jamais_sur_cent_mille_messages_multicanaux()
    {
        // La réutilisation d'un couple clé-nonce en GCM est catastrophique :
        // elle révèle le xor des clairs et permet de forger des étiquettes.
        var (a, _) = Pair();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 100_000; i++)
        {
            var channel = (byte)(i % 24);
            var frame = a.Seal(channel, kind: 0x08, "x"u8);
            var nonce = Convert.ToHexString(SecureChannel.NonceOf(frame));

            Assert.True(seen.Add(nonce), $"nonce répété au message {i}");
        }
    }

    [Fact]
    public void Un_rejeu_est_refuse()
    {
        var (a, b) = Pair();
        var frame = a.Seal(channel: 1, kind: 0x01, "une fois"u8);

        Assert.True(b.TryOpen(frame, out _, out _, out _, out _));
        Assert.False(b.TryOpen(frame, out _, out _, out _, out var why));
        Assert.Contains("rejeu", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Les_canaux_ont_chacun_leur_compteur()
    {
        // Le transport livre en ordre par canal, pas entre canaux : un compteur
        // global ferait rejeter des messages parfaitement légitimes.
        var (a, b) = Pair();

        var surUn = a.Seal(channel: 1, kind: 0x01, "un"u8);
        var surDeux = a.Seal(channel: 2, kind: 0x01, "deux"u8);

        Assert.True(b.TryOpen(surDeux, out _, out _, out _, out var why1), why1);
        Assert.True(b.TryOpen(surUn, out _, out _, out _, out var why2), why2);
    }

    [Fact]
    public void Une_etiquette_alteree_est_refusee()
    {
        var (a, b) = Pair();
        var frame = a.Seal(channel: 1, kind: 0x01, "charge"u8);
        frame[^1] ^= 0x01;

        Assert.False(b.TryOpen(frame, out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_entete_altere_est_refuse()
    {
        // L'en-tête est donnée associée : le modifier doit casser l'ouverture,
        // sans quoi on pourrait faire passer un message pour un autre type.
        var (a, b) = Pair();
        var frame = a.Seal(channel: 1, kind: 0x01, "charge"u8);
        frame[0] = 0x02;

        Assert.False(b.TryOpen(frame, out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_trame_venant_d_une_autre_session_est_refusee()
    {
        var (a, _) = Pair();
        var (_, autre) = Pair();

        Assert.False(autre.TryOpen(a.Seal(1, 0x01, "charge"u8), out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(20)]
    public void Une_trame_trop_courte_est_refusee_sans_lever(int length)
    {
        var (_, b) = Pair();
        Assert.False(b.TryOpen(new byte[length], out _, out _, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_canal_hors_bornes_est_refuse_a_l_emission()
    {
        var (a, _) = Pair();
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Seal(SecureChannel.MaxChannels, 0x01, "x"u8));
    }

    [Fact]
    public void Un_message_vide_reste_chiffre_et_authentifie()
    {
        var (a, b) = Pair();
        var frame = a.Seal(channel: 0, kind: 0x0C, []);

        Assert.True(b.TryOpen(frame, out var kind, out _, out var payload, out var why), why);
        Assert.Equal(0x0C, kind);
        Assert.Empty(payload);
    }
}
