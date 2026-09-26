using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Integration;

namespace Linkpearl.Ui;

/// <summary>
/// Un badge aux pieds de chaque pair visible dont l'apparence n'est pas encore là.
/// </summary>
/// <remarks>
/// Aux pieds et non au-dessus de la tête : la hauteur d'un personnage va du
/// Lalafell au Roegadyn, alors que la position de l'objet est celle du sol,
/// la même pour tous. Et le nom du personnage occupe déjà le dessus.
///
/// En deux temps, parce que l'ObjectTable ne se lit que depuis le thread du
/// framework : <see cref="Update"/> y calcule les positions à l'écran, et
/// <see cref="Draw"/> ne fait que peindre ce qui a été préparé.
/// </remarks>
internal sealed class TransferOverlay(IGameGui gameGui, IObjectTable objects)
{
    private IReadOnlyList<(Vector2 At, TransferBadge Badge)> _marks = [];

    public void Update(
        bool enabled, IReadOnlyList<PeerStatus> statuses, IReadOnlyList<NearbyPlayer> nearby, PairBook book)
    {
        if (enabled is false || statuses.Count == 0 || nearby.Count == 0)
        {
            _marks = [];
            return;
        }

        var marks = new List<(Vector2, TransferBadge)>();

        foreach (var status in statuses)
        {
            if (TransferBadge.Of(status) is not { } badge)
                continue;

            // L'empreinte annoncée par le pair, ou à défaut celle épinglée à la
            // première rencontre : avant le premier échange, seule la seconde existe.
            var fingerprint = status.View.Fingerprint ?? book.Find(status.Peer)?.PinnedFingerprint;

            if (fingerprint is not { } print)
                continue;

            var player = nearby.FirstOrDefault(candidate => candidate.Fingerprint == print);

            if (player is null)
                continue;

            // L'emplacement a pu être repris par un autre objet depuis l'instantané.
            var target = objects[player.Object.ObjectIndex];

            if (target is null || target.GameObjectId != player.Object.StableId)
                continue;

            if (gameGui.WorldToScreen(target.Position, out var screen))
                marks.Add((screen, badge));
        }

        _marks = marks;
    }

    public void Draw()
    {
        var marks = _marks;

        if (marks.Count == 0)
            return;

        // L'arrière-plan : le badge appartient à la scène, il passe sous les
        // fenêtres au lieu de les recouvrir.
        var dl = ImGui.GetBackgroundDrawList();

        using var font = Fonts.PushSmall();

        foreach (var (at, badge) in marks)
            DrawBadge(dl, at, badge);
    }

    private static void DrawBadge(ImDrawListPtr dl, Vector2 at, TransferBadge badge)
    {
        var text = badge.Progress is null ? $"{badge.Label}…" : badge.Label;
        var size = ImGui.CalcTextSize(text);

        var padX = Theme.S(8f);
        var padY = Theme.S(4f);
        var bar = badge.Progress is null ? 0f : Theme.S(4f);

        var width = Math.Max(size.X + padX * 2f, Theme.S(120f));
        var height = size.Y + padY * 2f + bar;

        // Un peu sous les pieds, centré.
        var min = new Vector2(MathF.Round(at.X - width * 0.5f), MathF.Round(at.Y + Theme.S(6f)));
        var max = min + new Vector2(width, height);
        var rounding = Theme.S(6f);

        // La nuit, presque opaque : le badge flotte sur n'importe quel décor du
        // jeu, et en deçà de 85 % le texte se perd dans une scène claire.
        dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.BgBase, 0.90f)), rounding);
        dl.AddRect(min, max, ImGui.GetColorU32(Theme.Alpha(Theme.Accent, 0.55f)), rounding);

        dl.AddText(new Vector2(min.X + (width - size.X) * 0.5f, min.Y + padY),
                   ImGui.GetColorU32(Theme.Text), text);

        if (badge.Progress is not { } progress)
            return;

        var track = new Vector2(min.X + padX, max.Y - padY - bar * 0.5f);
        var end = new Vector2(max.X - padX, track.Y + bar * 0.5f);

        dl.AddRectFilled(track, end, ImGui.GetColorU32(Theme.Alpha(Theme.Text, 0.15f)), bar);

        if (progress > 0f)
        {
            var filled = new Vector2(track.X + (end.X - track.X) * progress, end.Y);
            dl.AddRectFilled(track, filled, ImGui.GetColorU32(Theme.Accent), bar);
        }
    }
}
