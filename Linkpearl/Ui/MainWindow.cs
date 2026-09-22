using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Linkpearl.Core.Identity;
using Linkpearl.Integration;

namespace Linkpearl.Ui;

/// <summary>
/// La fenêtre principale.
/// </summary>
/// <remarks>
/// <b>Aucune clé n'y est montrée.</b> L'utilisateur voit des noms de
/// personnage, qui sont l'identité qui l'intéresse : il veut voir les mods de
/// quelqu'un qu'il a devant lui, pas d'une suite hexadécimale.
///
/// Le dessin ne fait que lire un état préparé ailleurs. Rien ici n'interroge le
/// réseau ni l'ObjectTable : une image bloquée, c'est le jeu qui saccade.
/// </remarks>
public sealed class MainWindow : Window
{
    private readonly PairingService _pairing;
    private readonly PresenceService _presence;
    private readonly PluginState _state;
    private readonly Configuration _configuration;
    private readonly Action<NearbyPlayer> _requestPair;
    private readonly Action<IncomingRequest> _accept;
    private readonly Action<IncomingRequest> _decline;

    public MainWindow(
        PairingService pairing, PresenceService presence, PluginState state, Configuration configuration,
        Action<NearbyPlayer> requestPair, Action<IncomingRequest> accept, Action<IncomingRequest> decline)
        : base("Linkpearl")
    {
        _pairing = pairing;
        _presence = presence;
        _state = state;
        _configuration = configuration;
        _requestPair = requestPair;
        _accept = accept;
        _decline = decline;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 280),
            MaximumSize = new Vector2(900, 1200),
        };
    }

    public override void Draw()
    {
        DrawStatus();
        ImGui.Separator();

        using var tabs = ImRaii.TabBar("linkpearl-tabs");

        if (tabs.Success is false)
            return;

        DrawRequestsTab();
        DrawNearbyTab();
        DrawPairsTab();
        DrawSettingsTab();
    }

    private void DrawStatus()
    {
        var connected = _presence.Connected;

        using (ImRaii.PushColor(ImGuiCol.Text, connected ? Green : Red))
            ImGui.TextUnformatted(connected ? "Connecté au rendez-vous" : "Hors ligne");

        if (connected is false && _presence.LastFailure is { } failure)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({failure})");
        }

        if (_configuration.Discoverable is false)
            ImGui.TextDisabled("Vous n'êtes pas signalé aux autres joueurs.");
    }

    private void DrawRequestsTab()
    {
        var requests = _presence.PeekRequests();

        using var tab = ImRaii.TabItem(requests.Count > 0 ? $"Demandes ({requests.Count})###req" : "Demandes###req");

        if (tab.Success is false)
            return;

        if (requests.Count == 0)
        {
            ImGui.TextWrapped("Aucune demande en attente.");
            return;
        }

        foreach (var request in requests)
        {
            using var id = ImRaii.PushId(request.Id.ToHex());

            ImGui.TextUnformatted($"{request.CharacterName} souhaite se synchroniser avec vous.");

            // Le repère qui compte : cette personne est-elle devant vous ?
            var visible = _state.Nearby.Any(p =>
                string.Equals(p.Name, request.CharacterName, StringComparison.Ordinal));

            using (ImRaii.PushColor(ImGuiCol.Text, visible ? Green : Orange))
                ImGui.TextUnformatted(visible ? "Ce personnage est visible autour de vous." : "Ce personnage n'est pas visible d'ici.");

            if (ImGui.Button("Accepter"))
                _accept(request);

            ImGui.SameLine();

            if (ImGui.Button("Refuser"))
                _decline(request);

            ImGui.Separator();
        }
    }

    private void DrawNearbyTab()
    {
        using var tab = ImRaii.TabItem("Autour de vous");

        if (tab.Success is false)
            return;

        if (_state.Nearby.Count == 0)
        {
            ImGui.TextWrapped("Personne en vue.");
            return;
        }

        var known = _pairing.Book.All.Select(p => p.Id).ToHashSet();

        foreach (var player in _state.Nearby)
        {
            using var id = ImRaii.PushId(player.Object.ObjectIndex);

            var usesLinkpearl = _presence.Detected.ContainsKey(player.Fingerprint);

            using (ImRaii.PushColor(ImGuiCol.Text, usesLinkpearl ? Green : Grey))
                ImGui.TextUnformatted(usesLinkpearl ? "●" : "○");

            ImGui.SameLine();
            ImGui.TextUnformatted(player.Name);

            if (usesLinkpearl is false)
                continue;

            ImGui.SameLine();

            if (ImGui.Button("Demander le pairage"))
                _requestPair(player);
        }

        ImGui.Separator();
        ImGui.TextDisabled("● utilise Linkpearl   ○ ne l'utilise pas, ou ne se signale pas");
    }

    private void DrawPairsTab()
    {
        using var tab = ImRaii.TabItem("Pairs");

        if (tab.Success is false)
            return;

        var pairs = _pairing.Book.All.ToList();

        if (pairs.Count == 0)
        {
            ImGui.TextWrapped(
                "Aucun pair. Ciblez quelqu'un qui utilise Linkpearl dans l'onglet « Autour de vous », "
              + "et demandez-lui le pairage.");
            return;
        }

        foreach (var pair in pairs)
        {
            using var id = ImRaii.PushId(pair.Id.ToHex());

            ImGui.TextUnformatted(pair.DisplayName);
            ImGui.SameLine();
            ImGui.TextDisabled(pair.Paused ? "en pause" : pair.Trust.ToString());
        }
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Réglages");

        if (tab.Success is false)
            return;

        var discoverable = _configuration.Discoverable;

        if (ImGui.Checkbox("Me signaler aux autres joueurs", ref discoverable))
        {
            _configuration.Discoverable = discoverable;
            _configuration.Save();
        }

        ImGui.TextWrapped(
            "Sans cela, personne ne peut vous reconnaître ni vous adresser une demande. "
          + "Avec, l'opérateur du service de rendez-vous peut savoir que votre personnage est en ligne : "
          + "pour qu'un inconnu puisse vous reconnaître, il faut bien que quelque chose soit calculable "
          + "à partir de votre nom.");

        ImGui.Separator();

        var host = _configuration.RendezvousHost;

        if (ImGui.InputText("Rendez-vous", ref host, 128))
        {
            _configuration.RendezvousHost = host;
            _configuration.Save();
        }

        ImGui.TextWrapped(
            "Le service qui aide deux joueurs à se trouver. Il ne voit ni vos fichiers ni vos apparences. "
          + "Vous et vos pairs devez utiliser le même.");
    }

    private static readonly Vector4 Green = new(0.4f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.4f, 0.4f, 1f);
    private static readonly Vector4 Orange = new(0.95f, 0.7f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.5f, 0.5f, 0.5f, 1f);
}

/// <summary>
/// L'état que l'interface lit.
/// </summary>
/// <remarks>
/// Préparé par une tâche de fond, jamais calculé pendant le dessin : une image
/// bloquée, c'est le jeu qui saccade.
/// </remarks>
public sealed class PluginState
{
    public IReadOnlyList<NearbyPlayer> Nearby { get; set; } = [];

    public NearbyPlayer? Self { get; set; }
}
