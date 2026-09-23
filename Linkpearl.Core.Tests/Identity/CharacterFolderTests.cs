using Linkpearl.Core.Identity;
using Xunit;

namespace Linkpearl.Core.Tests.Identity;

/// <summary>
/// Le dossier d'un personnage : stable d'une session à l'autre, distinct d'un
/// personnage à l'autre, et qui ne laisse pas lire l'identifiant d'origine.
/// </summary>
public class CharacterFolderTests
{
    [Fact]
    public void Le_meme_personnage_retombe_sur_le_meme_dossier()
        => Assert.Equal(CharacterFolder.Name(0x0123456789ABCDEF), CharacterFolder.Name(0x0123456789ABCDEF));

    [Fact]
    public void Deux_personnages_ne_partagent_pas_leur_dossier()
        => Assert.NotEqual(CharacterFolder.Name(1), CharacterFolder.Name(2));

    [Fact]
    public void Le_nom_est_un_chemin_utilisable()
    {
        var name = CharacterFolder.Name(42);

        Assert.Equal(CharacterFolder.Length, name.Length);
        Assert.All(name, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }

    [Fact]
    public void L_identifiant_ne_se_lit_pas_dans_le_nom()
    {
        // Le dossier voyage dans les captures d'écran et les journaux partagés,
        // là où l'identifiant de contenu n'a rien à faire.
        const ulong contentId = 0x0123456789ABCDEF;

        Assert.DoesNotContain("0123456789abcdef", CharacterFolder.Name(contentId));
        Assert.DoesNotContain(contentId.ToString(), CharacterFolder.Name(contentId));
    }

    [Fact]
    public void Personne_de_connecte_na_pas_de_dossier()
        => Assert.Throws<ArgumentOutOfRangeException>(() => CharacterFolder.Name(0));
}
