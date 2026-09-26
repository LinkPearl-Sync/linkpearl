using Dalamud.Bindings.ImGui;
using Linkpearl.Ui.Components;
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

        // Pastille d'état, à gauche : la perle quand le service répond, un
        // point éteint sinon. Sans halo, la barre est trop basse pour lui.
        var dot = new Vector2(origin.X + Theme.S(Theme.PadWindowX), mid);

        if (status.Connected)
            Surface.Pearl(dl, dot, Theme.S(4.5f), glow: false);
        else
            dl.AddCircleFilled(dot, Theme.S(3.5f), ImGui.GetColorU32(Theme.TextFaint));

        // Nommer l'état : un point de trois pixels n'a jamais rien dit à
        // personne, et le plugin sert pour de bon sans rendez-vous joignable.
        var left = status.Connected
            ? status.Character
            : status.Failure is { } failure ? $"hors ligne, {failure}" : "hors ligne";

        // Décompte des pairs, à droite. Mesuré d'abord : c'est lui qui borne
        // la place laissée au texte de gauche.
        var right = status.Pairs switch
        {
            0 => null,
            _ when status.Applied > 0 => $"{status.Applied} / {status.Pairs} pairs visibles",
            _ => $"{status.Pairs} pair{(status.Pairs > 1 ? "s" : "")}",
        };

        var rightWidth = right is null ? 0f : ImGui.CalcTextSize(right).X + Theme.S(Theme.GapL);
        var leftX      = origin.X + Theme.S(Theme.PadWindowX + 10f);
        var room       = end.X - Theme.S(Theme.PadWindowX) - rightWidth - leftX;

        // Une fenêtre étroite rognait la panne en plein mot : on l'abrège, et
        // le texte entier reste au survol.
        var shown     = Ellipsize(left, room);
        var leftSize  = ImGui.CalcTextSize(shown);
        var leftStart = new Vector2(leftX, mid - leftSize.Y * 0.5f);

        dl.AddText(leftStart, ImGui.GetColorU32(status.Connected ? Theme.TextMuted : Theme.TextFaint), shown);

        if (right is not null)
        {
            var size = ImGui.CalcTextSize(right);

            dl.AddText(new Vector2(end.X - size.X - Theme.S(Theme.PadWindowX), mid - size.Y * 0.5f),
                ImGui.GetColorU32(Theme.TextFaint), right);
        }

        var hoveringLeft = ImGui.IsMouseHoveringRect(leftStart, leftStart + leftSize);

        ImGui.Dummy(new Vector2(width, height));

        if (hoveringLeft && ReferenceEquals(shown, left) is false)
            Feedback.Tooltip(() =>
            {
                // La police réduite de la barre est encore poussée : l'infobulle se lit au corps normal.
                using var body = Fonts.PushBody();
                Text.Body(left);
            });
    }

    /// <summary>Le texte tel quel s'il tient dans la largeur, sinon abrégé par « … ».</summary>
    private static string Ellipsize(string text, float room)
    {
        if (ImGui.CalcTextSize(text).X <= room)
            return text;

        const string ellipsis = "…";

        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + ellipsis).X > room)
            length--;

        return text[..length].TrimEnd(' ', ',') + ellipsis;
    }
}
