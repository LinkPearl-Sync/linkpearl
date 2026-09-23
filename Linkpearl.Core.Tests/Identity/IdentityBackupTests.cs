using System.Text;
using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

/// <summary>
/// Le fichier de sauvegarde d'identité.
/// </summary>
/// <remarks>
/// Il vient du disque et non du réseau, mais il se partage : un fichier reçu
/// par message privé peut avoir été fabriqué. Chaque champ qui deviendra un
/// chemin ou une taille d'allocation se vérifie donc comme s'il venait d'un pair.
/// </remarks>
public class IdentityBackupTests
{
    private static readonly BackupEntry Alice = new(
        "0123456789abcdef", [1, 2, 3, 4], Encoding.UTF8.GetBytes("[]"));

    private static readonly BackupEntry Bob = new(
        "fedcba9876543210", [9, 8, 7], Encoding.UTF8.GetBytes("[{\"x\":1}]"));

    /// <summary>Peu d'itérations : le coût réel se teste ailleurs, ici il ralentirait tout.</summary>
    private const int FastIterations = IdentityBackup.MinIterations;

    [Fact]
    public void Sans_mot_de_passe_le_fichier_se_relit_tel_quel()
    {
        var file = IdentityBackup.Write([Alice, Bob], password: null);

        Assert.False(IdentityBackup.IsProtected(file));

        var read = IdentityBackup.Read(file, password: null);

        Assert.Null(read.Failure);
        AssertSame([Alice, Bob], read.Entries);
    }

    [Fact]
    public void Avec_mot_de_passe_le_fichier_se_relit_avec_le_meme()
    {
        var file = IdentityBackup.Write([Alice, Bob], "nacre", FastIterations);

        Assert.True(IdentityBackup.IsProtected(file));

        var read = IdentityBackup.Read(file, "nacre");

        Assert.Null(read.Failure);
        AssertSame([Alice, Bob], read.Entries);
    }

    [Fact]
    public void Avec_mot_de_passe_le_contenu_ne_se_lit_pas_en_clair()
    {
        var file = IdentityBackup.Write([Bob], "nacre", FastIterations);

        Assert.DoesNotContain("fedcba9876543210", Encoding.ASCII.GetString(file));
    }

    [Fact]
    public void Un_mauvais_mot_de_passe_est_refuse()
    {
        var file = IdentityBackup.Write([Alice], "nacre", FastIterations);

        var read = IdentityBackup.Read(file, "perle");

        Assert.NotNull(read.Failure);
        Assert.Empty(read.Entries);
    }

    [Fact]
    public void Un_fichier_protege_lu_sans_mot_de_passe_le_demande()
    {
        var file = IdentityBackup.Write([Alice], "nacre", FastIterations);

        var read = IdentityBackup.Read(file, password: null);

        Assert.True(read.NeedsPassword);
        Assert.Empty(read.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nacre")]
    public void Un_octet_modifie_fait_rejeter_le_fichier(string? password)
    {
        var file = IdentityBackup.Write([Alice, Bob], password, FastIterations);

        file[^5] ^= 0x40;

        Assert.NotNull(IdentityBackup.Read(file, password).Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("nacre")]
    public void Un_fichier_tronque_est_rejete_sans_exception(string? password)
    {
        var file = IdentityBackup.Write([Alice, Bob], password, FastIterations);

        for (var length = 0; length < file.Length; length += 7)
            Assert.NotNull(IdentityBackup.Read(file.AsSpan(0, length), password).Failure);
    }

    [Fact]
    public void Un_fichier_quelconque_est_rejete()
    {
        var read = IdentityBackup.Read(Encoding.UTF8.GetBytes("ceci n'est pas une sauvegarde"), null);

        Assert.NotNull(read.Failure);
    }

    [Theory]
    [InlineData("../../../../evil")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdeg")]
    [InlineData("01234567/9abcdef")]
    public void Un_nom_de_dossier_qui_n_est_pas_une_empreinte_est_refuse_a_l_ecriture(string folder)
    {
        Assert.Throws<ArgumentException>(
            () => IdentityBackup.Write([Alice with { Folder = folder }], null));
    }

    [Fact]
    public void Un_nom_de_dossier_forge_dans_le_fichier_fait_tout_rejeter()
    {
        var file = IdentityBackup.Write([Alice, Bob], password: null);

        // Le dossier de Bob devient « ../../../../abcd », de même longueur.
        var at = IndexOf(file, "fedcba9876543210"u8);
        "../../../../abcd"u8.CopyTo(file.AsSpan(at));
        Reseal(file);

        var read = IdentityBackup.Read(file, null);

        Assert.NotNull(read.Failure);
        Assert.Empty(read.Entries);
    }

    [Fact]
    public void Deux_fois_le_meme_personnage_est_refuse()
    {
        Assert.Throws<ArgumentException>(() => IdentityBackup.Write([Alice, Alice], null));
    }

    [Fact]
    public void Un_nombre_d_iterations_demesure_est_refuse_a_la_lecture()
    {
        // Un fichier fabriqué pourrait demander des milliards d'itérations et
        // geler le jeu au moment où l'on tape le mot de passe.
        var file = IdentityBackup.Write([Alice], "nacre", FastIterations);

        BitConverter.GetBytes(int.MaxValue).CopyTo(file, IdentityBackup.IterationsOffset);

        var read = IdentityBackup.Read(file, "nacre");

        Assert.NotNull(read.Failure);
    }

    [Fact]
    public void Une_sauvegarde_vide_n_a_pas_de_sens()
    {
        Assert.Throws<ArgumentException>(() => IdentityBackup.Write([], null));
    }

    private static void AssertSame(IReadOnlyList<BackupEntry> expected, IReadOnlyList<BackupEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Folder, actual[i].Folder);
            Assert.Equal(expected[i].Identity, actual[i].Identity);
            Assert.Equal(expected[i].Pairs, actual[i].Pairs);
        }
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var at = haystack.AsSpan().IndexOf(needle);
        Assert.True(at >= 0);
        return at;
    }

    /// <summary>Recalcule le contrôle d'un fichier sans mot de passe après une retouche.</summary>
    /// <remarks>
    /// Sans cela, le test ne prouverait que la détection d'une altération, déjà
    /// couverte : c'est la validation du nom qui doit refuser ici.
    /// </remarks>
    private static void Reseal(byte[] file)
    {
        var body = file.AsSpan(IdentityBackup.PlainHeaderLength, file.Length - IdentityBackup.PlainHeaderLength - 32);
        System.Security.Cryptography.SHA256.HashData(body, file.AsSpan(file.Length - 32));
    }
}
