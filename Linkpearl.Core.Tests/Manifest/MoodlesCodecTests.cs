using System.Security.Cryptography;
using Linkpearl.Core.Manifest;
using MemoryPack;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

// Réplique exacte de Moodles.Data.MyStatus (f4b7578) : mêmes membres, même ordre, mêmes types.
public enum TestStatusType { Positive, Negative, Special }

public enum TestChainTrigger { Dispel, HitSomething }

[MemoryPackable]
public partial class TestMyStatus
{
    public Guid GUID = Guid.NewGuid();
    public int IconID;
    public string Title = "";
    public string Description = "";
    public string CustomFXPath = "";
    public long ExpiresAt;
    public TestStatusType Type;
    public uint Modifiers;
    public int Stacks = 1;
    public int StackSteps = 0;
    public Guid ChainedStatus = Guid.Empty;
    public TestChainTrigger ChainTrigger;
    public string Applier = "";
    public string Dispeller = "";
}

public class MoodlesCodecTests
{
    private static readonly MemoryPackSerializerOptions Options = new() { StringEncoding = StringEncoding.Utf16 };

    private static List<TestMyStatus> Sample() =>
    [
        new()
        {
            IconID = 210456, Title = "Fatigué", Description = "Une longue nuit", CustomFXPath = "vfx/common/eff/x.avfx",
            ExpiresAt = 1_790_000_000_000, Type = TestStatusType.Negative, Modifiers = 3, Stacks = 2, StackSteps = 1,
            ChainedStatus = Guid.NewGuid(), ChainTrigger = TestChainTrigger.HitSomething,
            Applier = "Prénom Nom@Monde", Dispeller = "Autre Nom@Monde",
        },
        new() { IconID = 1, Title = "", Description = "" },
    ];

    [Fact]
    public void Decode_puis_encode_redonne_les_memes_octets_que_MemoryPack()
    {
        var original = MemoryPackSerializer.Serialize(Sample(), Options);

        Assert.True(MoodlesCodec.TryDecode(original, out var statuses));
        Assert.Equal(original, MoodlesCodec.Encode(statuses));
    }

    [Fact]
    public void Ce_que_nous_encodons_se_relit_par_MemoryPack()
    {
        Assert.True(MoodlesCodec.TryDecode(MemoryPackSerializer.Serialize(Sample(), Options), out var statuses));

        var back = MemoryPackSerializer.Deserialize<List<TestMyStatus>>(MoodlesCodec.Encode(statuses), Options)!;

        Assert.Equal("Fatigué", back[0].Title);
        Assert.Equal(2, back[0].Stacks);
    }

    [Fact]
    public void Des_donnees_tronquees_sont_refusees_sans_exception()
    {
        var bytes = MemoryPackSerializer.Serialize(Sample(), Options);

        for (var length = 0; length < bytes.Length; length++)
            Assert.False(MoodlesCodec.TryDecode(bytes.AsSpan(0, length), out _), $"longueur {length}");
    }

    [Fact]
    public void Un_objet_d_une_version_future_est_refuse()
    {
        var bytes = MemoryPackSerializer.Serialize(Sample(), Options);
        bytes[4] = MoodlesCodec.MemberCount + 1;   // en-tête du premier objet, juste après le nombre (int32)

        Assert.False(MoodlesCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Trop_de_statuts_sont_refuses()
    {
        var many = Enumerable.Range(0, MoodlesCodec.MaxStatuses + 1).Select(_ => new TestMyStatus()).ToList();

        Assert.False(MoodlesCodec.TryDecode(MemoryPackSerializer.Serialize(many, Options), out _));
    }

    [Fact]
    public void Le_nettoyage_vide_les_noms_et_le_vfx()
    {
        var key = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var raw = Convert.ToBase64String(MemoryPackSerializer.Serialize(Sample(), Options));

        var clean = MoodlesSanitizer.Sanitize(raw, key)!;
        var back = MemoryPackSerializer.Deserialize<List<TestMyStatus>>(Convert.FromBase64String(clean), Options)!;

        Assert.All(back, s => Assert.Equal("", s.Applier));
        Assert.All(back, s => Assert.Equal("", s.Dispeller));
        Assert.All(back, s => Assert.Equal("", s.CustomFXPath));
        Assert.True(MoodlesSanitizer.IsSanitizedAndBounded(clean));
        Assert.False(MoodlesSanitizer.IsSanitizedAndBounded(raw));
    }

    [Fact]
    public void Les_identifiants_sont_stables_pour_un_personnage_et_differents_d_un_autre()
    {
        var sample = Sample();
        var raw = Convert.ToBase64String(MemoryPackSerializer.Serialize(sample, Options));

        Guid FirstId(byte[] key)
            => MemoryPackSerializer.Deserialize<List<TestMyStatus>>(
                Convert.FromBase64String(MoodlesSanitizer.Sanitize(raw, key)!), Options)![0].GUID;

        var alice = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var bob = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(FirstId(alice), FirstId(alice));
        Assert.NotEqual(FirstId(alice), FirstId(bob));
        Assert.NotEqual(sample[0].GUID, FirstId(alice));
    }

    [Fact]
    public void Une_liste_vide_de_moodles_reste_valide()
    {
        var key = MoodlesSanitizer.KeyFor(RandomNumberGenerator.GetBytes(32));
        var empty = Convert.ToBase64String(MemoryPackSerializer.Serialize(new List<TestMyStatus>(), Options));

        Assert.True(MoodlesSanitizer.IsSanitizedAndBounded(MoodlesSanitizer.Sanitize(empty, key)!));
    }
}
