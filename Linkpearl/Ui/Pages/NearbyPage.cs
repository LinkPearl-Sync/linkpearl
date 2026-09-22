using Dalamud.Bindings.ImGui;
using Linkpearl.Integration;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Les joueurs visibles autour de soi, et ceux d'entre eux qui utilisent
/// Linkpearl.
/// </summary>
/// <remarks>
/// C'est la page du pairage, et elle est volontairement la première : on se
/// paire avec quelqu'un qu'on a devant soi, pas avec une clé reçue par message.
/// </remarks>
internal sealed class NearbyPage(PluginState state, PresenceService presence, Action<NearbyPlayer> requestPair)
{
    public void Draw()
    {
        Text.Title("Autour de vous");
        Text.Small("Une pastille pleine signale un joueur qui utilise Linkpearl et se laisse trouver.");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        if (state.Nearby.Count == 0)
        {
            Feedback.EmptyState(
                Icons.Nearby,
                "Personne en vue",
                "Rapprochez-vous de quelqu'un : la liste suit ce que votre personnage voit.");

            return;
        }

        foreach (var player in state.Nearby)
        {
            var uses = presence.Detected.ContainsKey(player.Fingerprint);

            using var card = Card.Begin(
                $"nearby_{player.Object.ObjectIndex}",
                CardTone.Interactive,
                accent: uses ? Theme.Accent : null);

            Feedback.StatusDot(uses ? Theme.Online : Theme.TextFaint);
            ImGui.SameLine(0f, Theme.S(Theme.GapM));
            Text.H2(player.Name);

            Text.Small(uses ? "utilise Linkpearl" : "ne l'utilise pas, ou ne se signale pas");

            if (uses is false)
                continue;

            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            if (Btn.Draw("Demander le pairage", BtnTone.Primary, BtnSize.Small, Icons.Invite,
                         id: $"pair_{player.Object.ObjectIndex}"))
                requestPair(player);
        }
    }
}
