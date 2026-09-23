using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

/// <summary>
/// Reprise prudente de ce qui vivait sous l'ancien InternalName : on ne
/// remplace jamais rien, on n'efface jamais l'ancien, et on ne touche pas à
/// ce qui pourrait appartenir au plugin officiel de même ancien nom.
/// </summary>
public sealed class LegacyConfigAdoptionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "linkpearl-adoption-" + Guid.NewGuid().ToString("N"));

    public LegacyConfigAdoptionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Path_(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void Un_ancien_dossier_a_nous_est_repris_dans_un_dossier_neuf()
    {
        var legacyDir = Path_("Linkpearl");
        var characterDir = Path.Combine(legacyDir, "characters", "abc");
        Directory.CreateDirectory(characterDir);
        File.WriteAllBytes(Path.Combine(characterDir, "identity.key"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(legacyDir, "remote-collections.txt"), "une-collection");

        var newDir = Path_("LinkpearlSync");

        var adopted = LegacyConfigAdoption.TryAdoptDirectory(legacyDir, newDir);

        Assert.True(adopted);
        Assert.True(File.Exists(Path.Combine(newDir, "characters", "abc", "identity.key")));
        Assert.Equal("une-collection", File.ReadAllText(Path.Combine(newDir, "remote-collections.txt")));

        // L'ancien dossier reste intact : rien n'a été déplacé.
        Assert.True(File.Exists(Path.Combine(characterDir, "identity.key")));
        Assert.True(File.Exists(Path.Combine(legacyDir, "remote-collections.txt")));
    }

    [Fact]
    public void Un_dossier_neuf_deja_non_vide_n_est_pas_touche()
    {
        var legacyDir = Path_("Linkpearl");
        Directory.CreateDirectory(Path.Combine(legacyDir, "characters", "abc"));
        File.WriteAllBytes(Path.Combine(legacyDir, "characters", "abc", "identity.key"), [1]);

        var newDir = Path_("LinkpearlSync");
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(newDir, "deja-la.txt"), "ne pas toucher");

        var adopted = LegacyConfigAdoption.TryAdoptDirectory(legacyDir, newDir);

        Assert.False(adopted);
        Assert.False(File.Exists(Path.Combine(newDir, "characters", "abc", "identity.key")));
        Assert.True(File.Exists(Path.Combine(newDir, "deja-la.txt")));
    }

    [Fact]
    public void Un_ancien_dossier_sans_characters_n_est_pas_repris()
    {
        // Celui du plugin officiel NotNite, au même ancien nom : pas de
        // sous-dossier characters, donc on n'y touche pas.
        var legacyDir = Path_("Linkpearl");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "mumble-settings.json"), "{}");

        var newDir = Path_("LinkpearlSync");

        var adopted = LegacyConfigAdoption.TryAdoptDirectory(legacyDir, newDir);

        Assert.False(adopted);
        Assert.False(Directory.Exists(newDir));
    }

    [Fact]
    public void Un_dossier_neuf_absent_avec_un_ancien_absent_n_est_pas_repris()
    {
        var adopted = LegacyConfigAdoption.TryAdoptDirectory(Path_("Linkpearl"), Path_("LinkpearlSync"));

        Assert.False(adopted);
    }

    [Fact]
    public void Un_ancien_fichier_de_configuration_a_nous_est_repris()
    {
        var legacyFile = Path_("Linkpearl.json");
        File.WriteAllText(legacyFile,
            """{"RendezvousHost":"83.228.242.221","CacheQuotaBytes":53687091200,"Discoverable":true}""");

        var newFile = Path_("LinkpearlSync.json");

        var adopted = LegacyConfigAdoption.TryAdoptConfigFile(legacyFile, newFile);

        Assert.True(adopted);
        Assert.Equal(File.ReadAllText(legacyFile), File.ReadAllText(newFile));
        Assert.True(File.Exists(legacyFile));
    }

    [Fact]
    public void Un_fichier_sans_nos_cles_n_est_pas_repris()
    {
        // Celui du plugin officiel NotNite, au même ancien nom de fichier.
        var legacyFile = Path_("Linkpearl.json");
        File.WriteAllText(legacyFile, """{"Version":1,"MumbleHost":"x"}""");

        var newFile = Path_("LinkpearlSync.json");

        var adopted = LegacyConfigAdoption.TryAdoptConfigFile(legacyFile, newFile);

        Assert.False(adopted);
        Assert.False(File.Exists(newFile));
    }

    [Fact]
    public void Un_fichier_neuf_deja_existant_n_est_jamais_ecrase()
    {
        var legacyFile = Path_("Linkpearl.json");
        File.WriteAllText(legacyFile, """{"RendezvousHost":"x","Discoverable":true}""");

        var newFile = Path_("LinkpearlSync.json");
        File.WriteAllText(newFile, "deja-la");

        var adopted = LegacyConfigAdoption.TryAdoptConfigFile(legacyFile, newFile);

        Assert.False(adopted);
        Assert.Equal("deja-la", File.ReadAllText(newFile));
    }

    [Fact]
    public void Un_json_illisible_n_est_pas_repris()
    {
        var legacyFile = Path_("Linkpearl.json");
        File.WriteAllText(legacyFile, "{ceci n'est pas du json");

        var newFile = Path_("LinkpearlSync.json");

        var adopted = LegacyConfigAdoption.TryAdoptConfigFile(legacyFile, newFile);

        Assert.False(adopted);
        Assert.False(File.Exists(newFile));
    }

    [Fact]
    public void Un_ancien_fichier_absent_n_est_pas_repris()
    {
        var adopted = LegacyConfigAdoption.TryAdoptConfigFile(Path_("Linkpearl.json"), Path_("LinkpearlSync.json"));

        Assert.False(adopted);
    }
}
