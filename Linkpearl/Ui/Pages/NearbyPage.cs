using Dalamud.Bindings.ImGui;
using Linkpearl.Integration;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Les joueurs autour de soi qui utilisent Linkpearl.
/// </summary>
/// <remarks>
/// C'est la page du pairage, et elle est volontairement la première : on se
/// paire avec quelqu'un qu'on a devant soi, pas avec une clé reçue par message.
///
/// Seuls ceux qui utilisent le plugin sont listés. Une place bondée compte
/// couramment quarante joueurs, dont deux ou trois sont concernés : les lister
/// tous obligerait à chercher, pour une information dont on ne fait rien. Le
/// décompte des autres reste affiché, parce qu'une liste vide alors qu'il y a
/// foule ressemblerait à une panne de détection.
/// </remarks>
internal sealed class NearbyPage(
    PluginState state, PresenceService presence, PairingService pairing, Action<NearbyPlayer> requestPair)
{
    public void Draw()
    {
        var users  = state.Nearby.Where(player => presence.Detected.ContainsKey(player.Fingerprint)).ToList();
        var others = state.Nearby.Count - users.Count;

        Text.Title("Autour de vous");
        Text.Small("Les joueurs à portée qui utilisent Linkpearl et se laissent trouver.");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        if (users.Count == 0)
        {
            Feedback.EmptyState(
                Icons.Nearby,
                "Personne qui utilise Linkpearl",
                others > 0
                    ? $"{others} joueur{(others > 1 ? "s" : "")} à portée, aucun ne se signale."
                    : "Rapprochez-vous de quelqu'un : la liste suit ce que votre personnage voit.");

            return;
        }

        // Épinglée à la première rencontre, l'empreinte est ce qui relie un
        // joueur visible à une entrée du carnet. Un pair jamais rencontré n'en a
        // pas encore : on proposera le pairage, et le carnet refusera le doublon.
        var known = pairing.Book.All
            .Where(pair => pair.PinnedFingerprint is not null)
            .Select(pair => pair.PinnedFingerprint!.Value)
            .ToHashSet();

        foreach (var player in users)
        {
            var paired = known.Contains(player.Fingerprint);

            using var card = Card.Begin(
                $"nearby_{player.Object.ObjectIndex}",
                CardTone.Interactive,
                accent: paired ? Theme.Online : Theme.Accent);

            Feedback.StatusDot(paired ? Theme.Online : Theme.Accent);
            ImGui.SameLine(0f, Theme.S(Theme.GapM));
            Text.H2(player.Name);

            ImGui.Dummy(Theme.S(0f, Theme.GapXs));

            if (paired)
            {
                // Proposer de se pairer avec quelqu'un qui l'est déjà enverrait
                // une demande que le carnet rejetterait, sans que rien ne le dise.
                Chip.Draw("déjà pairé", Theme.Online, Icons.Applied);
                continue;
            }

            if (Btn.Draw("Demander le pairage", BtnTone.Primary, BtnSize.Small, Icons.Invite,
                         id: $"pair_{player.Object.ObjectIndex}"))
                requestPair(player);
        }

        if (others == 0)
            return;

        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        Text.Small($"{others} autre{(others > 1 ? "s" : "")} joueur{(others > 1 ? "s" : "")} à portée.",
                   Theme.TextFaint);
    }
}
