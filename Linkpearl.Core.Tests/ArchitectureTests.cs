using System.Reflection;
using Xunit;

namespace Linkpearl.Core.Tests;

/// <summary>
/// Fait respecter la règle qui rend tout le reste testable : le noyau ne
/// référence jamais Dalamud.
/// </summary>
/// <remarks>
/// Sans ce test, la dérive est une affaire de mois : un jour quelqu'un a besoin
/// d'un index d'objet « juste ici », et l'ensemble du noyau cesse de compiler
/// sous Linux, donc cesse d'être testé.
/// </remarks>
public class ArchitectureTests
{
    private static IReadOnlyList<(string Name, string Source)> CoreSources()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".cs", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return (name, reader.ReadToEnd());
            })
            .ToList();
    }

    [Fact]
    public void Le_test_inspecte_reellement_des_sources()
    {
        // Garde-fou du garde-fou : sans lui, une erreur d'embarquement ferait
        // passer tous les tests suivants au vert en n'inspectant rien.
        var sources = CoreSources();

        Assert.NotEmpty(sources);
        Assert.Contains(sources, s => s.Name.EndsWith("GamePathPolicy.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using Dalamud", "le noyau cesserait de compiler sous Linux")]
    [InlineData("using ImGuiNET", "l'interface n'a rien à faire dans le noyau")]
    [InlineData("using FFXIVClientStructs", "le noyau ne connaît pas la mémoire du jeu")]
    [InlineData("using System.Windows", "le noyau doit rester multiplateforme")]
    public void Le_noyau_n_importe_rien_du_jeu_ni_de_l_interface(string forbidden, string why)
    {
        var offenders = CoreSources()
            .Where(s => s.Source.Contains(forbidden, StringComparison.Ordinal))
            .Select(s => s.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"« {forbidden} » interdit ({why}) dans : {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.UtcNow")]
    [InlineData("DateTimeOffset.Now")]
    [InlineData("DateTimeOffset.UtcNow")]
    public void Le_noyau_ne_lit_pas_l_horloge_directement(string forbidden)
    {
        // L'heure passe par IClock : un debounce ou une fenetre de ticket qui
        // lirait l'horloge systeme ne serait pas testable de maniere
        // deterministe, et c'est exactement la sorte de test qui devient
        // intermittent six mois plus tard.
        var offenders = CoreSources()
            .Where(s => s.Source.Contains(forbidden, StringComparison.Ordinal))
            .Select(s => s.Name)
            .ToList();

        Assert.True(offenders.Count == 0, $"« {forbidden} » interdit dans : {string.Join(", ", offenders)}");
    }
}
