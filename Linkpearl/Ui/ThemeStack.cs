using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace Linkpearl.Ui;

/// <summary>
/// Compteur de <c>PushStyleColor</c> et <c>PushStyleVar</c>, dépilé en un appel.
/// </summary>
/// <remarks>
/// Une classe et non un <c>ref struct</c> : le style est poussé dans
/// <c>Window.PreDraw()</c> et dépilé dans <c>Window.PostDraw()</c>, deux
/// méthodes distinctes, donc l'objet doit survivre entre les deux dans un champ.
///
/// <see cref="PopAll"/> est idempotent : si un <c>Draw()</c> lève, Dalamud
/// appelle quand même <c>PostDraw</c> et le style ne fuit pas sur les fenêtres
/// des autres plugins.
/// </remarks>
internal sealed class ThemeStack
{
    private int _colors;
    private int _vars;

    public ThemeStack Color(ImGuiCol idx, Vector4 color)
    {
        ImGui.PushStyleColor(idx, color);
        _colors++;
        return this;
    }

    public ThemeStack Var(ImGuiStyleVar idx, float value)
    {
        ImGui.PushStyleVar(idx, value);
        _vars++;
        return this;
    }

    public ThemeStack Var(ImGuiStyleVar idx, Vector2 value)
    {
        ImGui.PushStyleVar(idx, value);
        _vars++;
        return this;
    }

    public void PopAll()
    {
        if (_colors > 0) ImGui.PopStyleColor(_colors);
        if (_vars   > 0) ImGui.PopStyleVar(_vars);

        _colors = 0;
        _vars   = 0;
    }
}
