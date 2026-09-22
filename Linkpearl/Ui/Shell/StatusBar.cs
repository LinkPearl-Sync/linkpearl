using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Linkpearl.Ui.Shell;

/// <summary>Ce que la barre d'état montre, préparé par l'appelant.</summary>
/// <remarks>
/// Un enregistrement plutôt que des services : la barre ne doit rien
/// interroger. Une image bloquée, c'est le jeu qui saccade.
/// </remarks>
internal readonly record struct ShellStatus(
    bool Connected,
    string? Failure,
    string Character,
    int Pairs,
    int Applied);

/// <summary>
/// Bandeau de pied de fenêtre : état du rendez-vous, personnage suivi, pairs.
/// </summary>
internal static class StatusBar
{
    public static void Draw(ShellStatus status)
    {
        var height = Theme.S(Theme.StatusBarHeight);
        var origin = ImGui.GetCursorScreenPos();
        var width  = ImGui.GetContentRegionAvail().X;
        var end    = new Vector2(origin.X + width, origin.Y + height);
        var dl     = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, end, ImGui.GetColorU32(Theme.BgSidebar),
            Theme.S(Theme.RadiusWindow), ImDrawFlags.RoundCornersBottom);

        dl.AddLine(origin, new Vector2(end.X, origin.Y), ImGui.GetColorU32(Theme.BorderSoft), 1f);

        using var font = Fonts.PushSmall();

        var mid = origin.Y + height * 0.5f;

        // Pastille d'état, à gauche.
        dl.AddCircleFilled(
            new Vector2(origin.X + Theme.S(Theme.PadWindowX), mid),
            Theme.S(3.5f),
            ImGui.GetColorU32(status.Connected ? Theme.Online : Theme.TextFaint));

        // Nommer l'état : un point de trois pixels n'a jamais rien dit à
        // personne, et le plugin sert pour de bon sans rendez-vous joignable.
        var left = status.Connected
            ? status.Character
            : status.Failure is { } failure ? $"hors ligne, {failure}" : "hors ligne";

        var leftSize = ImGui.CalcTextSize(left);

        dl.AddText(
            new Vector2(origin.X + Theme.S(Theme.PadWindowX + 10f), mid - leftSize.Y * 0.5f),
            ImGui.GetColorU32(status.Connected ? Theme.TextMuted : Theme.TextFaint),
            left);

        // Décompte des pairs, à droite.
        if (status.Pairs > 0)
        {
            var text = status.Applied > 0
                ? $"{status.Applied} / {status.Pairs} pairs visibles"
                : $"{status.Pairs} pair{(status.Pairs > 1 ? "s" : "")}";

            var size = ImGui.CalcTextSize(text);

            dl.AddText(new Vector2(end.X - size.X - Theme.S(Theme.PadWindowX), mid - size.Y * 0.5f),
                ImGui.GetColorU32(Theme.TextFaint), text);
        }

        ImGui.Dummy(new Vector2(width, height));
    }
}
