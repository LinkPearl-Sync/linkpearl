using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Identity;

namespace Linkpearl.Integration;

/// <summary>
/// L'entrée de Linkpearl dans la barre de statut du jeu : le symbole HQ, suivi
/// du nombre de pairs à portée.
/// </summary>
/// <remarks>
/// Le symbole HQ est le glyphe du plugin. Il vient de la police du jeu
/// (<see cref="SeIconChar.HighQuality"/>), et non d'une police embarquée : la
/// barre est dessinée par le jeu, qui l'a déjà.
///
/// Le compte se fait sur les empreintes épinglées au carnet, croisées avec ce
/// que le personnage voit : c'est la réponse à « qui, parmi les miens, est là »,
/// indépendamment de l'état du transfert.
///
/// <see cref="Update"/> ne s'appelle que depuis le thread du framework : la
/// barre est un nœud de l'interface du jeu.
/// </remarks>
internal sealed class StatusBarEntry : IDisposable
{
    private static readonly string Glyph = SeIconChar.HighQuality.ToIconString();

    private readonly IDtrBarEntry _entry;
    private int? _shown;

    public StatusBarEntry(IDtrBar bar, Action open)
    {
        _entry = bar.Get("Linkpearl");
        _entry.OnClick = _ => open();
        Show(0);
    }

    public void Update(IReadOnlyList<NearbyPlayer> nearby, IEnumerable<PairRecord> pairs)
    {
        var pinned = pairs
            .Where(pair => pair.Trust is not PairTrust.Blocked && pair.PinnedFingerprint is not null)
            .Select(pair => pair.PinnedFingerprint!.Value)
            .ToHashSet();

        Show(nearby.Count(player => pinned.Contains(player.Fingerprint)));
    }

    private void Show(int count)
    {
        // Réécrire le texte à chaque appel marquerait le nœud comme modifié
        // pour rien, et le jeu le recomposerait à chaque fois.
        if (_shown == count)
            return;

        _shown = count;
        _entry.Text = $"{Glyph} {count}";
        _entry.Tooltip = count switch
        {
            0 => "Linkpearl : aucun pair à portée",
            1 => "Linkpearl : 1 pair à portée",
            _ => $"Linkpearl : {count} pairs à portée",
        };
    }

    public void Dispose() => _entry.Remove();
}
