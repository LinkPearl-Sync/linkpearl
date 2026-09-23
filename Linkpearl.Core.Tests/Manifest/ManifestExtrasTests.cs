using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class ManifestExtrasTests
{
    private static CharacterManifest With(CharacterExtras? extras)
        => new(CharacterManifest.CurrentVersion,
               [new FileReplacement(["chara/equipment/e0001/model/c0101e0001_top.mdl"], BlobHash.OfContent("x"u8), 1)],
               "", null, extras);

    [Fact]
    public void Les_extras_font_l_aller_retour()
    {
        var extras = new CharacterExtras("{\"Bones\":{}}", "{\"DefaultOffset\":0.1}", "{\"Title\":\"x\"}", "AAAAAA==", "QQA=");

        var bytes = ManifestCodec.Compress(With(extras));

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.Equal(extras, back!.ExtrasOrNone);
    }

    [Fact]
    public void Un_manifeste_sans_extras_se_relit_sans_extras()
    {
        var bytes = ManifestCodec.Compress(With(null));

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.True(back!.ExtrasOrNone.IsEmpty);
    }

    [Fact]
    public void Un_manifeste_v1_d_un_ancien_client_est_accepte()
    {
        var v1 = With(null) with { Version = 1 };
        var bytes = ManifestCodec.Compress(v1);

        Assert.True(ManifestCodec.TryDecompress(bytes, Quotas.Default, out var back, out var why), why);
        Assert.True(ManifestValidator.TryAccept(back!, Quotas.Default, out var refus), refus);
        Assert.True(back!.ExtrasOrNone.IsEmpty);
    }

    [Fact]
    public void Un_titre_qui_change_change_l_empreinte_du_manifeste()
    {
        var before = With(new CharacterExtras(null, null, "{\"Title\":\"a\"}", null, null));
        var after = With(new CharacterExtras(null, null, "{\"Title\":\"b\"}", null, null));

        Assert.NotEqual(ManifestCodec.HashOf(before), ManifestCodec.HashOf(after));
    }

    [Fact]
    public void Des_extras_vides_ne_changent_pas_l_empreinte()
    {
        Assert.Equal(ManifestCodec.HashOf(With(null)), ManifestCodec.HashOf(With(CharacterExtras.None)));
    }

    [Fact]
    public void Un_extra_non_textuel_est_refuse()
    {
        var json = "{\"v\":2,\"r\":[],\"m\":\"\",\"g\":null,\"xt\":42}"u8.ToArray();
        using var output = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            brotli.Write(json);

        Assert.False(ManifestCodec.TryDecompress(output.ToArray(), Quotas.Default, out _, out _));
    }
}
