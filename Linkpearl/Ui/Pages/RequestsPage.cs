using Dalamud.Bindings.ImGui;
using Linkpearl.Integration;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Les demandes de pairage reçues.
/// </summary>
/// <remarks>
/// Le repère qui compte est affiché avant les boutons : cette personne est-elle
/// devant vous ? C'est la seule vérification qu'un joueur fera réellement, et
/// elle vaut mieux qu'une empreinte à comparer.
/// </remarks>
internal sealed class RequestsPage(
    PluginState state, PresenceService presence,
    Action<IncomingRequest> accept, Action<IncomingRequest> decline)
{
    public int Count => presence.PeekRequests().Count;

    public void Draw()
    {
        var requests = presence.PeekRequests();

        Text.Title("Demandes");
        Text.Small("Quelqu'un souhaite que vous vous voyiez mutuellement avec vos mods.");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        if (requests.Count == 0)
        {
            Feedback.EmptyState(
                Icons.Requests,
                "Aucune demande en attente",
                "Les demandes arrivent en jeu, sans quitter le client.");

            return;
        }

        foreach (var request in requests)
        {
            var visible = state.Nearby.Any(player =>
                string.Equals(player.Name, request.CharacterName, StringComparison.Ordinal));

            using var card = Card.Begin($"request_{request.Id.ToHex()}", accent: Theme.Accent);

            Text.H2(request.CharacterName);
            ImGui.Dummy(Theme.S(0f, Theme.GapXs));

            Chip.Draw(
                visible ? "visible autour de vous" : "pas visible d'ici",
                visible ? Theme.Online : Theme.Idle,
                visible ? Icons.Character : Icons.Warning);

            ImGui.Dummy(Theme.S(0f, Theme.GapM));

            if (Btn.Draw("Accepter", BtnTone.Success, BtnSize.Small, Icons.Accept,
                         id: $"accept_{request.Id.ToHex()}"))
                accept(request);

            ImGui.SameLine();

            if (Btn.Draw("Refuser", BtnTone.Ghost, BtnSize.Small, Icons.Decline,
                         id: $"decline_{request.Id.ToHex()}"))
                decline(request);
        }
    }
}
