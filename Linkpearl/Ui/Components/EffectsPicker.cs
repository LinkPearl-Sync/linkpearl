using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Linkpearl.Core.Safety;

namespace Linkpearl.Ui.Components;

/// <summary>
/// Animations, VFX et sons, en trois boutons allumés ou éteints.
/// </summary>
/// <remarks>
/// Des boutons et non des cases : l'état se lit à la couleur, chaque catégorie
/// porte l'icône qu'elle a dans la barre de titre, et les trois tiennent sur une
/// ligne. Le même composant sert au Public, à un pair et à un membre de groupe,
/// pour qu'un joueur n'apprenne le geste qu'une fois.
/// </remarks>
internal static class EffectsPicker
{
    /// <summary>Dessine les trois boutons. Rend les catégories modifiées, ou null si rien n'a été cliqué.</summary>
    public static TransientCategories? Draw(TransientCategories current, string id)
    {
        (string Label, FontAwesomeIcon Icon, bool On, Func<bool, TransientCategories> With)[] toggles =
        [
            ("Animations", Icons.Animations, current.Animations, on => current with { Animations = on }),
            ("VFX",        Icons.Vfx,        current.Vfx,        on => current with { Vfx = on }),
            ("Sons",       Icons.Sounds,     current.Sounds,     on => current with { Sounds = on }),
        ];

        TransientCategories? changed = null;

        for (var i = 0; i < toggles.Length; i++)
        {
            var (label, icon, on, with) = toggles[i];

            if (i > 0)
                ImGui.SameLine(0f, Theme.S(Theme.GapXs));

            if (Btn.Draw(label, on ? BtnTone.Selected : BtnTone.Secondary, BtnSize.Small, icon, id: $"{id}_{i}",
                         tooltip: on ? "Reçus. Cliquer pour les bloquer." : "Bloqués. Cliquer pour les recevoir."))
                changed = with(on is false);
        }

        return changed;
    }
}
