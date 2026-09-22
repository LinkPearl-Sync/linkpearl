using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Safety;

/// <summary>
/// Le chemin de jeu est la clé que l'on passe à Penumbra. C'est la seule chaîne
/// venant du réseau qui atteigne un autre plugin, donc le test le plus rentable
/// du projet.
/// </summary>
public class GamePathPolicyTests
{
    private static string Normalize(string raw)
    {
        Assert.True(GamePathPolicy.TryNormalize(raw, Quotas.Default, out var ok, out var why),
                    $"aurait du etre accepte, rejet : {why}");
        return ok;
    }

    private static string Reject(string raw)
    {
        Assert.False(GamePathPolicy.TryNormalize(raw, Quotas.Default, out var ok, out var why),
                     $"aurait du etre rejete, normalise en : {ok}");
        Assert.NotNull(why);
        return why!;
    }

    // --- Chemins legitimes, tires de vrais mods ---

    [Theory]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl")]
    [InlineData("chara/equipment/e6111/material/v0001/mt_c0201e6111_top_a.mtrl")]
    [InlineData("chara/human/c0201/obj/face/f0002/texture/--c0201f0002_etc_d.tex")]
    [InlineData("chara/human/c0101/skeleton/base/b0001/skl_c0101b0001.sklb")]
    [InlineData("chara/human/c0801/obj/body/b0001/b0001.imc")]
    [InlineData("chara/xls/charamake/human.cmp")]
    [InlineData("bg/ffxiv/sea_s1/fld/s1f1/texture/s1f1_b0_bg00.tex")]
    [InlineData("vfx/common/eff/dk_healing0h.avfx")]
    [InlineData("ui/icon/062000/062101.tex")]
    public void Un_chemin_de_jeu_reel_est_accepte_tel_quel(string path)
    {
        Assert.Equal(path, Normalize(path));
    }

    [Fact]
    public void Le_double_tiret_des_textures_ffxiv_n_est_pas_confondu_avec_autre_chose()
    {
        const string path = "chara/human/c0201/obj/face/f0002/texture/--c0201f0002_etc_d.tex";
        Assert.Equal(path, Normalize(path));
    }

    [Fact]
    public void La_casse_est_normalisee_plutot_que_rejetee()
    {
        // Penumbra peut rendre des chemins en casse mixte. Les rejeter ferait
        // echouer des mods legitimes ; on normalise, ce qui rend aussi le hash
        // du manifeste stable entre deux machines.
        Assert.Equal("chara/equipment/e0101/model/c0101e0101_top.mdl",
                     Normalize("Chara/Equipment/E0101/Model/C0101E0101_Top.MDL"));
    }

    // --- Traversee de repertoire ---

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("chara/../../../etc/passwd")]
    [InlineData("chara/equipment/../../../../windows/system32/drivers/etc/hosts")]
    [InlineData("chara/..")]
    [InlineData("..")]
    public void La_traversee_de_repertoire_est_rejetee(string path)
    {
        Reject(path);
    }

    [Fact]
    public void Le_segment_point_simple_est_rejete()
    {
        Reject("chara/./equipment/e0101/model/c0101e0101_top.mdl");
    }

    // --- Separateurs et racines ---

    [Theory]
    [InlineData("chara\\equipment\\e0101\\model\\c0101e0101_top.mdl")]
    [InlineData("chara/equipment\\e0101/model/c0101e0101_top.mdl")]
    public void L_antislash_est_rejete_et_jamais_traduit_en_slash(string path)
    {
        // Traduire l'antislash serait une porte : un pair pourrait alors ecrire
        // un chemin que la validation a vu autrement que le systeme de fichiers.
        Reject(path);
    }

    [Theory]
    [InlineData("/chara/equipment/e0101/model/c0101e0101_top.mdl")]
    [InlineData("c:/windows/system32/config/sam")]
    [InlineData("c:\\windows\\system32\\config\\sam")]
    [InlineData("//serveur/partage/fichier.tex")]
    [InlineData("\\\\serveur\\partage\\fichier.tex")]
    public void Un_chemin_absolu_ou_unc_est_rejete(string path)
    {
        Reject(path);
    }

    [Fact]
    public void Un_slash_final_est_rejete()
    {
        Reject("chara/equipment/e0101/model/");
    }

    [Fact]
    public void Un_segment_vide_est_rejete()
    {
        Reject("chara//equipment/e0101/model/c0101e0101_top.mdl");
    }

    // --- Jeu de caracteres ---

    [Theory]
    [InlineData("chara/equipment/e0101/model/c0101 e0101_top.mdl")]      // espace
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl\0")]      // octet nul
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl\n")]      // saut de ligne
    [InlineData("chara/equipment/e0101/model/c\u0430101e0101_top.mdl")]   // a cyrillique
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl?x=1")]
    [InlineData("chara/equipment/e0101/model/c0101e0101_top.mdl:stream")] // flux alternatif NTFS
    [InlineData("chara/equipment/e0101/model/c0101e0101_top<.mdl")]
    public void Tout_caractere_hors_du_jeu_autorise_est_rejete(string path)
    {
        Reject(path);
    }

    [Fact]
    public void Un_chemin_vide_ou_blanc_est_rejete()
    {
        Reject("");
        Reject("   ");
    }

    // --- Plafonds ---

    [Fact]
    public void Un_chemin_trop_long_est_rejete()
    {
        var path = "chara/" + new string('a', Quotas.Default.MaxGamePathLength) + ".tex";
        Reject(path);
    }

    [Fact]
    public void Un_chemin_trop_profond_est_rejete()
    {
        var path = string.Join('/', Enumerable.Repeat("chara", Quotas.Default.MaxGamePathDepth + 2)) + ".tex";
        Reject(path);
    }

    [Fact]
    public void Les_plafonds_sont_ceux_passes_en_parametre_et_non_des_constantes()
    {
        var etroit = Quotas.Default with { MaxGamePathDepth = 3 };
        Assert.False(GamePathPolicy.TryNormalize(
            "chara/equipment/e0101/model/c0101e0101_top.mdl", etroit, out _, out _));
    }

    // --- Prefixe connu ---

    [Theory]
    [InlineData("truc/equipment/e0101/model/c0101e0101_top.mdl")]
    [InlineData("tmp/charge_utile.tex")]
    [InlineData("chara2/equipment/e0101/model/c0101e0101_top.mdl")]
    public void Un_chemin_hors_des_racines_connues_du_jeu_est_rejete(string path)
    {
        Reject(path);
    }

    [Fact]
    public void Le_rejet_nomme_sa_raison()
    {
        // La raison part dans le journal : sans elle, un manifeste refuse est
        // indiagnosticable a distance.
        Assert.Contains("traversée", Reject("chara/../../etc/passwd"), System.StringComparison.OrdinalIgnoreCase);
    }
}
