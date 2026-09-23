using Linkpearl.Core.Cache;
using Linkpearl.Core.Manifest;
using Linkpearl.Core.Safety;
using Xunit;

namespace Linkpearl.Core.Tests.Manifest;

public class ManifestBuilderTests
{
    private const string Meta = "bWV0YQ==";
    private const string Glam = "Z2xhbW91cmVy";

    private static ResolvedFile File(string gamePath, string content)
        => new(gamePath, BlobHash.OfContent(System.Text.Encoding.UTF8.GetBytes(content)), content.Length);

    private static readonly ResolvedFile Torse =
        File("chara/equipment/e0101/model/c0101e0101_top.mdl", "modele-torse");
    private static readonly ResolvedFile Peau =
        File("chara/human/c0201/obj/body/b0001/texture/--c0201b0001_base.tex", "texture-peau");

    [Fact]
    public void Un_manifeste_reprend_les_fichiers_resolus()
    {
        var result = ManifestBuilder.Build([Torse, Peau], Meta, Glam, Quotas.Default);

        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Manifest.Replacements.Count);
        Assert.Equal(Meta, result.Manifest.MetaManipulations);
        Assert.Equal(Glam, result.Manifest.GlamourerState);
    }

    [Fact]
    public void Un_meme_contenu_vise_par_plusieurs_chemins_ne_donne_qu_une_entree()
    {
        // C'est toute la raison du regroupement par hash : une texture reference
        // par six chemins de jeu, c'est un blob a transferer et non six.
        var a = File("chara/equipment/e0101/model/c0101e0101_top.mdl", "identique");
        var b = File("chara/equipment/e0102/model/c0101e0102_top.mdl", "identique");

        var result = ManifestBuilder.Build([a, b], Meta, Glam, Quotas.Default);

        var entry = Assert.Single(result.Manifest.Replacements);
        Assert.Equal(2, entry.GamePaths.Count);
    }

    [Fact]
    public void L_ordre_d_arrivee_ne_change_pas_le_hash_du_manifeste()
    {
        // Sans cette propriete, on renverrait un manifeste « change » a chaque
        // recalcul, et le diff ne servirait plus a rien.
        var direct = ManifestBuilder.Build([Torse, Peau], Meta, Glam, Quotas.Default).Manifest;
        var inverse = ManifestBuilder.Build([Peau, Torse], Meta, Glam, Quotas.Default).Manifest;

        Assert.Equal(ManifestCodec.HashOf(direct), ManifestCodec.HashOf(inverse));
    }

    [Fact]
    public void Un_contenu_different_change_le_hash_du_manifeste()
    {
        var avant = ManifestBuilder.Build([Torse], Meta, Glam, Quotas.Default).Manifest;
        var apres = ManifestBuilder.Build(
            [File("chara/equipment/e0101/model/c0101e0101_top.mdl", "autre-modele")],
            Meta, Glam, Quotas.Default).Manifest;

        Assert.NotEqual(ManifestCodec.HashOf(avant), ManifestCodec.HashOf(apres));
    }

    [Fact]
    public void L_etat_glamourer_entre_dans_le_hash()
    {
        var a = ManifestBuilder.Build([Torse], Meta, Glam, Quotas.Default).Manifest;
        var b = ManifestBuilder.Build([Torse], Meta, "YXV0cmU=", Quotas.Default).Manifest;

        Assert.NotEqual(ManifestCodec.HashOf(a), ManifestCodec.HashOf(b));
    }

    [Fact]
    public void La_casse_du_chemin_est_normalisee_dans_le_manifeste()
    {
        var result = ManifestBuilder.Build(
            [File("Chara/Equipment/E0101/Model/C0101E0101_Top.MDL", "x")], Meta, Glam, Quotas.Default);

        Assert.Equal("chara/equipment/e0101/model/c0101e0101_top.mdl",
                     Assert.Single(result.Manifest.Replacements).GamePaths[0]);
    }

    [Fact]
    public void Un_chemin_local_invalide_est_ecarte_et_rapporte_plutot_que_de_tout_faire_echouer()
    {
        // A la construction on ecarte : un seul mod tordu ne doit pas priver
        // l'utilisateur de toute synchronisation. A la reception, au contraire,
        // le manifeste entier est rejete.
        var result = ManifestBuilder.Build(
            [Torse, File("../../../etc/passwd", "x")], Meta, Glam, Quotas.Default);

        Assert.Single(result.Manifest.Replacements);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("../../../etc/passwd", skipped.GamePath);
        Assert.Contains("traversée", skipped.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_extension_hors_perimetre_est_ecartee_et_rapportee()
    {
        var result = ManifestBuilder.Build(
            [Torse, File("shader/sm5/shpk/character.shpk", "x")], Meta, Glam, Quotas.Default);

        Assert.Single(result.Manifest.Replacements);
        Assert.Contains("shader", Assert.Single(result.Skipped).Reason, StringComparison.OrdinalIgnoreCase);
    }
}

public class ManifestCodecTests
{
    private static CharacterManifest Sample()
        => ManifestBuilder.Build(
            [
                new ResolvedFile("chara/equipment/e0101/model/c0101e0101_top.mdl",
                                 BlobHash.OfContent("a"u8), 1),
                new ResolvedFile("chara/human/c0201/obj/body/b0001/texture/--c0201b0001_base.tex",
                                 BlobHash.OfContent("b"u8), 2),
            ],
            "bWV0YQ==", "Z2xhbW91cmVy", Quotas.Default).Manifest;

    [Fact]
    public void L_aller_retour_compresse_conserve_le_manifeste()
    {
        var origine = Sample();
        var compresse = ManifestCodec.Compress(origine);

        Assert.True(ManifestCodec.TryDecompress(compresse, Quotas.Default, out var relu, out var why), why);
        Assert.Equal(ManifestCodec.HashOf(origine), ManifestCodec.HashOf(relu!));
        Assert.Equal(origine.Replacements.Count, relu!.Replacements.Count);
        Assert.Equal(origine.MetaManipulations, relu.MetaManipulations);
        Assert.Equal(origine.GlamourerState, relu.GlamourerState);
    }

    [Fact]
    public void L_encodage_canonique_est_stable_octet_pour_octet()
    {
        Assert.Equal(ManifestCodec.Encode(Sample()), ManifestCodec.Encode(Sample()));
    }

    [Fact]
    public void La_compression_reduit_reellement_un_manifeste_realiste()
    {
        var files = Enumerable.Range(0, 400)
            .Select(i => new ResolvedFile(
                $"chara/equipment/e{i:D4}/model/c0101e{i:D4}_top.mdl",
                BlobHash.OfContent(System.Text.Encoding.UTF8.GetBytes($"contenu{i}")), i))
            .ToList();

        var manifest = ManifestBuilder.Build(files, "bWV0YQ==", "Z2xhbQ==", Quotas.Default).Manifest;

        var brut = ManifestCodec.Encode(manifest).Length;
        var compresse = ManifestCodec.Compress(manifest).Length;

        Assert.True(compresse < brut / 2, $"brut {brut} octets, compressé {compresse}");
    }

    [Fact]
    public void Un_manifeste_illisible_est_refuse_sans_lever()
    {
        Assert.False(ManifestCodec.TryDecompress("ceci n'est pas du brotli"u8, Quotas.Default, out _, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Une_bombe_de_decompression_est_arretee_au_plafond()
    {
        // Un pair peut envoyer 1 Mo qui se detend en plusieurs Go. La lecture
        // s'arrete au plafond au lieu de remplir la memoire du processus du jeu.
        var enorme = new byte[64 * 1024 * 1024];
        using var sortie = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(sortie, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            brotli.Write(enorme);

        Assert.False(ManifestCodec.TryDecompress(sortie.ToArray(), Quotas.Default, out _, out var why));
        Assert.Contains("plafond", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_manifeste_recu_qui_viole_une_seule_regle_est_rejete_en_entier()
    {
        // Asymetrie voulue avec la construction : un rejet partiel donnerait un
        // personnage incoherent et masquerait une tentative.
        var hostile = new CharacterManifest(
            CharacterManifest.CurrentVersion,
            [
                new FileReplacement(["chara/equipment/e0101/model/c0101e0101_top.mdl"], BlobHash.OfContent("a"u8), 1),
                new FileReplacement(["../../../etc/passwd"], BlobHash.OfContent("b"u8), 2),
            ],
            "bWV0YQ==", null);

        Assert.False(ManifestValidator.TryAccept(hostile, Quotas.Default, out var why));
        Assert.NotNull(why);
    }

    [Fact]
    public void Un_manifeste_qui_depasse_le_plafond_d_entrees_est_rejete()
    {
        var etroit = Quotas.Default with { MaxReplacements = 1 };
        var manifest = Sample();

        Assert.False(ManifestValidator.TryAccept(manifest, etroit, out var why));
        Assert.Contains("plafond", why!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_manifeste_legitime_est_accepte()
    {
        Assert.True(ManifestValidator.TryAccept(Sample(), Quotas.Default, out var why), why);
    }

    [Fact]
    public void Une_version_de_manifeste_inconnue_est_rejetee()
    {
        var futur = Sample() with { Version = 9999 };
        Assert.False(ManifestValidator.TryAccept(futur, Quotas.Default, out _));
    }
}
