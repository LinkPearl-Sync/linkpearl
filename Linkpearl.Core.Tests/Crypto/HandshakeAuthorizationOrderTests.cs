using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Transport.Rendezvous;
using Xunit;

namespace Linkpearl.Core.Tests.Crypto;

/// <summary>
/// L'autorisation ne se consulte qu'après la preuve cryptographique.
/// </summary>
/// <remarks>
/// Pour un membre de groupe, autoriser épingle la clé présentée et la persiste.
/// Si l'autorisation passait avant la signature, n'importe quel correspondant
/// pourrait épingler une clé dont il ne détient pas la partie privée, rien
/// qu'en atteignant ce point du handshake : c'est ce que ces tests interdisent.
/// </remarks>
public sealed class HandshakeAuthorizationOrderTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Secret = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly PlayerFingerprint AlicePrint = PlayerFingerprint.Of("alice", 21);
    private static readonly PlayerFingerprint BobPrint = PlayerFingerprint.Of("bob", 21);

    private readonly ECDsa _identity = CryptoPrimitives.GenerateIdentity();
    private readonly byte[] _sealingKey = RandomNumberGenerator.GetBytes(CryptoPrimitives.KeyLength);
    private readonly byte[] _bindingKey = RandomNumberGenerator.GetBytes(CryptoPrimitives.KeyLength);
    private readonly byte[] _transcript = RandomNumberGenerator.GetBytes(32);

    private static ReadOnlySpan<byte> Context => HandshakeFormat.ResponderSignatureContext;

    public void Dispose() => _identity.Dispose();

    private byte[] Point => CryptoPrimitives.ExportPublicPoint(_identity);

    private byte[] ValidPayload()
        => HandshakeTranscript.SealAuthentication(_identity, Context, _transcript, _sealingKey, _bindingKey);

    /// <summary>
    /// Scelle une authentification dont une partie est altérée, sous les bonnes
    /// clés : le chiffrement passe, seule la preuve échoue.
    /// </summary>
    private byte[] TamperedPayload(int offset)
    {
        Assert.True(CryptoPrimitives.TryOpen(
            _sealingKey, HandshakeFormat.HandshakeNonce, ValidPayload(), _transcript, out var clear));

        clear[offset] ^= 0x01;
        return CryptoPrimitives.Seal(_sealingKey, HandshakeFormat.HandshakeNonce, clear, _transcript);
    }

    private byte[] BadSignature() => TamperedPayload(CryptoPrimitives.PublicPointLength + 3);

    private byte[] BadBinding()
        => TamperedPayload(CryptoPrimitives.PublicPointLength + CryptoPrimitives.SignatureLength + 3);

    private bool Open(byte[] sealedPayload, Func<byte[], bool> isAuthorized, out string? rejection)
        => HandshakeTranscript.TryOpenAuthentication(
            sealedPayload, Context, _transcript, _sealingKey, _bindingKey, isAuthorized, out _, out rejection);

    [Fact]
    public void Une_signature_invalide_n_atteint_jamais_l_autorisation()
    {
        var calls = 0;

        Assert.False(Open(BadSignature(), _ => { calls++; return true; }, out var why));
        Assert.Equal(0, calls);
        Assert.Contains("signature", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_liaison_invalide_n_atteint_jamais_l_autorisation()
    {
        var calls = 0;

        Assert.False(Open(BadBinding(), _ => { calls++; return true; }, out var why));
        Assert.Equal(0, calls);
        Assert.Contains("liaison", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_preuve_valide_consulte_l_autorisation_une_fois_avec_la_cle_prouvee()
    {
        var seen = new List<byte[]>();

        Assert.True(Open(ValidPayload(), key => { seen.Add(key); return true; }, out var why), why);
        Assert.Equal(Point, Assert.Single(seen));
    }

    [Fact]
    public void Une_preuve_valide_mais_refusee_par_le_carnet_reste_refusee()
    {
        // Paire directe : l'autorisation est une comparaison pure, son verdict
        // ne change pas avec l'ordre.
        Assert.False(Open(ValidPayload(), _ => false, out var why));
        Assert.Contains("carnet", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_preuve_invalide_n_epingle_rien_dans_le_carnet_de_groupe()
    {
        var book = new GroupBook(new FixedClock(T0));
        var group = GroupId.Of(Secret);
        Assert.True(book.TryAdd(new GroupRecord
        {
            Id = group,
            Name = "Essai",
            Secret = Secret,
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            JoinedAt = T0,
        }, out var refusal), refusal);

        var changes = 0;
        book.Changed += () => changes++;

        // Le pair tel que le planificateur le fabriquerait pour Bob vu par Alice.
        var memberSecret = GroupDerivation.MemberPairSecret(Secret, AlicePrint, BobPrint);
        var pair = new PairRecord
        {
            Id = GroupDerivation.RuntimeId(memberSecret),
            PairSecret = memberSecret,
            DisplayName = "Membre",
            Rendezvous = [new RendezvousAddress("rdv.exemple.ch", 47900)],
            Trust = PairTrust.Accepted,
            PairedAt = T0,
            PinnedFingerprint = BobPrint,
            Group = new GroupOrigin(group, AlicePrint, BobPrint),
        };
        IGroupGate gate = book;

        Assert.False(Open(BadSignature(), key => gate.Admits(pair, key), out _));
        Assert.False(Open(BadBinding(), key => gate.Admits(pair, key), out _));

        Assert.Equal(0, changes);
        Assert.Empty(book.Find(group)!.Members);

        // Et la vraie clé, une fois prouvée, s'épingle bien : le test ne passe
        // pas parce que le carnet refuserait tout.
        Assert.True(Open(ValidPayload(), key => gate.Admits(pair, key), out var why), why);
        Assert.Equal(1, changes);
        Assert.Equal(PeerId.Of(Point), book.Find(group)!.Members[BobPrint].Id);
    }
}
