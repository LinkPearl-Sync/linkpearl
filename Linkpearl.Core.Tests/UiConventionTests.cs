using System.Reflection;
using Xunit;

namespace Linkpearl.Core.Tests;

/// <summary>
/// Fait respecter les conventions de l'interface : rien ne contourne les composants.
/// </summary>
/// <remarks>
/// L'interface ne se teste pas sous Linux, elle dépend de Dalamud. Ses sources,
/// si : elles sont embarquées comme celles du noyau. Chaque règle tient la liste
/// des fichiers que la refonte n'a pas encore repris ; une liste qui ne rétrécit
/// plus est une refonte qui s'est arrêtée en route.
///
/// Le test ne dit pas si c'est joli. Il dit seulement qu'une case ImGui brute,
/// une couleur en dur ou un espacement laissé au défaut ne reviendront pas en
/// douce une fois l'écran repris.
/// </remarks>
public class UiConventionTests
{
    private const string Prefix = ".UiSources.";

    private static IReadOnlyList<(string File, string Source)> UiSources()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(Prefix, StringComparison.Ordinal)
                        && name.EndsWith(".cs", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var file = name[(name.IndexOf(Prefix, StringComparison.Ordinal) + Prefix.Length)..];
                return (file, reader.ReadToEnd());
            })
            .ToList();
    }

    /// <summary>Motif interdit, raison, fichiers exemptés pour de bon, fichiers pas encore repris.</summary>
    public static TheoryData<string, string, string[], string[]> Rules => new()
    {
        {
            "ImGui.Checkbox(", "un réglage oui/non passe par Toggle", [],
            ["Pages.GroupsPage.cs", "Pages.PairsPage.cs", "Pages.SettingsPage.cs"]
        },
        {
            "ImGui.CollapsingHeader(", "un en-tête repliable passe par Fold", [],
            ["Pages.NearbyPage.cs", "Pages.PairsPage.cs"]
        },
        {
            "ImGui.SameLine()", "l'espacement se dit, il ne se laisse pas au défaut d'ImGui", [],
            ["NameplateLegend.cs", "Pages.RequestsPage.cs", "Pages.SettingsPage.cs", "RequestToasts.cs"]
        },
        {
            "ImGui.Button(", "un bouton passe par Btn", ["Components.Btn.cs"], []
        },
        {
            "BtnTone.Primary", "l'orange est à l'action (BtnTone.Action), le choix au halo (BtnTone.Selected)", [], []
        },
        {
            "Hex(0x", "une couleur vient d'un jeton de Theme", ["Theme.cs"],
            ["NameplateGlyphs.cs", "Onboarding.OnboardingArt.cs"]
        },
    };

    [Fact]
    public void Le_test_inspecte_reellement_l_interface()
    {
        // Garde-fou du garde-fou : un préfixe de ressource mal écrit ferait
        // passer toutes les règles au vert en n'inspectant rien.
        var files = UiSources().Select(source => source.File).ToList();

        Assert.Contains("Theme.cs", files);
        Assert.Contains("Pages.GroupsPage.cs", files);
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void Rien_ne_contourne_les_composants(string forbidden, string why, string[] exempt, string[] pending)
    {
        var offenders = UiSources()
            .Where(source => source.Source.Contains(forbidden, StringComparison.Ordinal))
            .Select(source => source.File)
            .Where(file => exempt.Contains(file) is false)
            .ToHashSet();

        var unexpected = offenders.Except(pending).Order().ToList();

        Assert.True(unexpected.Count == 0,
            $"« {forbidden} » interdit ({why}) dans : {string.Join(", ", unexpected)}");

        // Un fichier repris doit quitter la liste : il pourrait sinon régresser
        // sans que rien ne tombe.
        var stale = pending.Except(offenders).Order().ToList();

        Assert.True(stale.Count == 0,
            $"Déjà repris, à retirer des fichiers en attente de « {forbidden} » : {string.Join(", ", stale)}");
    }
}
