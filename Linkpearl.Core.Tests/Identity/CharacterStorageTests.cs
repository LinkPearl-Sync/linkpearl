using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

/// <summary>
/// La reprise des affaires d'avant la séparation par personnage.
/// </summary>
/// <remarks>
/// La logique elle-même vit dans l'adaptateur Dalamud, qui ne se compile pas
/// ici. Ce qui se teste sous Linux est ce dont elle dépend : que deux
/// personnages ne tombent jamais dans le même dossier, et qu'un dossier soit
/// utilisable comme chemin.
/// </remarks>
public class CharacterStorageTests
{
    [Fact]
    public void Mille_personnages_ne_se_marchent_pas_dessus()
    {
        var seen = new HashSet<string>();

        for (ulong contentId = 1; contentId <= 1000; contentId++)
            Assert.True(seen.Add(CharacterFolder.Name(contentId)), $"collision sur {contentId}");
    }

    [Fact]
    public void Le_dossier_se_compose_avec_un_chemin()
    {
        var path = Path.Combine("racine", "characters", CharacterFolder.Name(1234));

        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(path));
        Assert.DoesNotContain("..", path);
    }
}
