using System.Buffers.Binary;
using System.Text;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

/// <summary>
/// Le contrôle de structure des fichiers transitoires reçus.
/// </summary>
/// <remarks>
/// Les en-têtes sont construits d'après les mesures faites sur 5 109 <c>.pap</c>
/// de mods réels (spec du 23 septembre 2026) : un premier en-tête relevé
/// donnait « pap », 0x20001, une animation, modèle 101, type 0, informations à
/// 26, Havok à 66, puis la marque d'un fichier Havok.
/// </remarks>
public class TransientFileCheckTests
{
    private static readonly byte[] HavokTag = Convert.FromHexString("1E0DB0CACEFA11D0");

    private static byte[] Pap(
        short animations = 1, byte type = 0, int padding = 0, string magic = "pap ", uint version = 0x20001,
        int? info = null, int? havok = null, int havokBytes = 64, bool havokMagic = true, int? footer = null)
    {
        var havokAt = havok ?? (26 + (40 * animations) + padding);
        var footerAt = footer ?? (havokAt + havokBytes);
        var file = new byte[Math.Max(footerAt + 16, havokAt + havokBytes)];

        Encoding.ASCII.GetBytes(magic).CopyTo(file, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), version);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(8), animations);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(10), 101);
        file[12] = type;
        file[13] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), info ?? 26);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), havokAt);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), footerAt);

        if (havokMagic && havokAt + 8 <= file.Length)
            HavokTag.CopyTo(file, havokAt);

        return file;
    }

    private static bool Check(string path, byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        return TransientFileCheck.IsWellFormed(path, stream, out _);
    }

    private const string PapPath = "chara/human/c0101/animation/a0001/bt_common/emote/pose01_loop.pap";

    [Fact]
    public void Un_pap_comme_ceux_des_mods_est_accepte() => Assert.True(Check(PapPath, Pap()));

    [Fact]
    public void Un_pap_sans_animation_est_accepte() => Assert.True(Check(PapPath, Pap(animations: 0)));

    [Fact]
    public void Un_remplissage_avant_havok_est_accepte() => Assert.True(Check(PapPath, Pap(padding: 9)));

    [Fact]
    public void Un_type_autre_qu_humain_est_accepte() => Assert.True(Check(PapPath, Pap(type: 1)));

    [Fact]
    public void Plusieurs_animations_sont_acceptees() => Assert.True(Check(PapPath, Pap(animations: 12)));

    [Fact]
    public void Une_autre_marque_est_refusee() => Assert.False(Check(PapPath, Pap(magic: "pbd ")));

    [Fact]
    public void Une_autre_version_est_refusee() => Assert.False(Check(PapPath, Pap(version: 0x30001)));

    [Fact]
    public void Des_informations_ailleurs_qu_a_26_sont_refusees() => Assert.False(Check(PapPath, Pap(info: 30)));

    [Fact]
    public void Trop_d_animations_sont_refusees() => Assert.False(Check(PapPath, Pap(animations: 1025)));

    [Fact]
    public void Havok_avant_la_fin_des_informations_est_refuse()
        => Assert.False(Check(PapPath, Pap(animations: 2, havok: 26 + 40)));

    [Fact]
    public void Une_fin_hors_du_fichier_est_refusee()
    {
        var file = Pap();
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), file.Length + 1);

        Assert.False(Check(PapPath, file));
    }

    [Fact]
    public void Une_section_havok_trop_courte_est_refusee() => Assert.False(Check(PapPath, Pap(havokBytes: 8)));

    [Fact]
    public void Une_section_havok_sans_marque_est_refusee() => Assert.False(Check(PapPath, Pap(havokMagic: false)));

    [Fact]
    public void Un_fichier_havok_brut_nomme_pap_est_refuse()
    {
        // Le seul refusé des 5 109 mesurés : un fichier Havok sans en-tête pap.
        var raw = new byte[200];
        HavokTag.CopyTo(raw, 0);

        Assert.False(Check(PapPath, raw));
    }

    [Fact]
    public void Un_pap_tronque_est_refuse_sans_exception()
    {
        var file = Pap();

        for (var length = 0; length < file.Length - 16; length++)
            Assert.False(Check(PapPath, file[..length]), $"longueur {length}");
    }

    [Theory]
    [InlineData("chara/action/emote/pose01.tmb", "TMLB")]
    [InlineData("vfx/common/eff/cmmn_aura01.avfx", "XFVA")]
    [InlineData("sound/voice/vo_emote/a.scd", "SEDBSSCF")]
    public void Les_autres_transitoires_portent_leur_marque(string path, string magic)
    {
        var good = Encoding.ASCII.GetBytes(magic).Concat(new byte[32]).ToArray();
        var bad = Encoding.ASCII.GetBytes(new string('Z', magic.Length)).Concat(new byte[32]).ToArray();

        Assert.True(Check(path, good));
        Assert.False(Check(path, bad));
        Assert.False(Check(path, good[..(magic.Length - 1)]));
    }

    [Fact]
    public void Un_atex_n_est_verifie_que_sur_sa_presence()
    {
        Assert.True(Check("vfx/common/texture/aura.atex", new byte[] { 0, 0, 0x80, 0 }));
        Assert.False(Check("vfx/common/texture/aura.atex", []));
    }

    [Fact]
    public void Un_fichier_statique_n_est_pas_concerne()
        => Assert.True(Check("chara/equipment/e0001/model/c0101e0001_top.mdl", [1, 2, 3]));
}
