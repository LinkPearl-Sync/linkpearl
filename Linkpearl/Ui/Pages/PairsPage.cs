using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Integration;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Le carnet de pairs, en listes par état.
/// </summary>
/// <remarks>
/// Des lignes et non des cartes : un carnet compte vite vingt entrées, et ce
/// qu'on y cherche est « qui est là », d'un coup d'œil. Les groupes répondent
/// à cette question avant même de lire un nom.
///
/// <b>Aucune clé n'y est montrée.</b> L'utilisateur voit des noms, qui sont
/// l'identité qui l'intéresse.
/// </remarks>
internal sealed class PairsPage(
    PairingService pairing, Func<IReadOnlyList<PeerStatus>> statuses,
    Action<PeerId, bool> setPaused, Action<PeerId> reapply, Action<PeerId> unpair,
    Action<PeerId, TransientCategories> setReceive, IServiceBans bans)
{
    private string _filter = "";

    /// <summary>Le pair dont le retrait attend un second clic, et jusqu'à quand.</summary>
    private (PeerId Id, DateTime Until)? _confirming;

    public int Count => pairing.Book.Listed.Count;

    public void Draw()
    {
        Text.Title("Pairs");
        Text.Small("Ce que chacun vous montre, et où en est le transfert.");
        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        var pairs = pairing.Book.Listed;

        if (pairs.Count == 0)
        {
            Feedback.EmptyState(
                Icons.Pairs,
                "Aucun pair",
                "Allez dans « Autour de vous » et demandez le pairage à quelqu'un qui utilise Linkpearl, "
              + "ou restaurez une sauvegarde depuis les réglages.");

            return;
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##filtre_pairs", "Filtrer par nom", ref _filter, 64);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var byPeer = statuses().ToDictionary(status => status.Peer);

        var shown = pairs
            .Where(pair => _filter.Length == 0
                        || pair.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var online  = new List<PairRecord>();
        var offline = new List<PairRecord>();
        var paused  = new List<PairRecord>();
        var blocked = new List<PairRecord>();

        foreach (var pair in shown)
        {
            byPeer.TryGetValue(pair.Id, out var status);

            (pair.Trust is PairTrust.Blocked ? blocked
             : pair.Paused ? paused
             : status is { State: not PeerSessionState.Disconnected } ? online
             : offline).Add(pair);
        }

        Group("En ligne", online, byPeer, defaultOpen: true);
        Group("Hors ligne", offline, byPeer, defaultOpen: true);
        Group("En pause", paused, byPeer, defaultOpen: false);
        Group("Bloqués", blocked, byPeer, defaultOpen: false);

        if (shown.Count == 0)
            Text.Small("aucun pair ne correspond au filtre.", Theme.TextFaint);
    }

    private void Group(string title, List<PairRecord> pairs, Dictionary<PeerId, PeerStatus> byPeer, bool defaultOpen)
    {
        if (pairs.Count == 0)
            return;

        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

        using var header = ImRaii.PushColor(ImGuiCol.Header, Theme.BgSurface)
                                 .Push(ImGuiCol.HeaderHovered, Theme.BgRaised)
                                 .Push(ImGuiCol.HeaderActive, Theme.BgRaised);

        if (ImGui.CollapsingHeader($"{title} ({pairs.Count})###groupe_{title}", flags) is false)
            return;

        using var table = ImRaii.Table($"pairs_{title}", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.PadOuterX);

        if (table.Success is false)
            return;

        ImGui.TableSetupColumn("état", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("nom", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("statut", ImGuiTableColumnFlags.WidthFixed, Theme.S(190f));
        ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, (ImGui.GetFrameHeight() * 4f) + Theme.S(Theme.GapS * 3f));

        foreach (var pair in pairs)
        {
            byPeer.TryGetValue(pair.Id, out var status);
            Row(pair, status);
        }
    }

    private void Row(PairRecord pair, PeerStatus? status)
    {
        var id = pair.Id.ToHex();

        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight() + Theme.S(Theme.GapS));

        ImGui.TableNextColumn();
        AlignToFrame();
        Feedback.StatusDot(Tint(pair, status));

        ImGui.TableNextColumn();
        AlignToFrame();
        ImGui.TextColored(Theme.Text, Glyphs.Safe(pair.DisplayName));

        if (bans.Status(pair.PinnedFingerprint ?? default) is { Verdict: BanVerdict.Listed })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            BanChip.Draw(bans, pair.PinnedFingerprint);
        }

        if (status is { State: not PeerSessionState.Disconnected, Route: { } route })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            DrawRoute(pair, route);
        }

        if (status is { FingerprintDisputed: true })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Text.Icon(Icons.Warning, Theme.Danger);
            Feedback.TooltipOnHover(
                "Ce pair annonce un autre personnage que celui auprès duquel vous vous êtes pairés. "
              + "Rien ne lui est appliqué.");
        }
        else if (status is { LastFailure: { } failure })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Text.Icon(Icons.Warning, Theme.Idle);
            Feedback.TooltipOnHover(failure);
        }

        ImGui.TableNextColumn();
        AlignToFrame();
        DrawState(pair, status);

        ImGui.TableNextColumn();

        if (pair.Paused)
        {
            if (Btn.Icon(Icons.Resume, $"resume_{id}", tooltip: "Reprendre"))
                setPaused(pair.Id, false);
        }
        else if (Btn.Icon(Icons.Paused, $"pause_{id}", tooltip: "Mettre en pause : la session se ferme et l'apparence est retirée"))
        {
            setPaused(pair.Id, true);
        }

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        var canReapply = status is { State: not PeerSessionState.Disconnected };

        if (Btn.Icon(Icons.Refresh, $"reapply_{id}",
                     tooltip: canReapply ? "Réappliquer : redemander la dernière apparence et la reposer" : "Hors ligne",
                     disabled: canReapply is false))
            reapply(pair.Id);

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        DrawReceive(pair, id);

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        // Retirer se confirme par un second clic : un carnet ne se vide pas
        // par un geste qui glisse.
        var confirming = _confirming is { } c && c.Id == pair.Id && c.Until > DateTime.UtcNow;

        if (Btn.Icon(Icons.Remove, $"unpair_{id}",
                     tone: confirming ? BtnTone.Danger : BtnTone.Ghost,
                     tooltip: confirming ? "Cliquer encore pour retirer ce pair" : "Retirer ce pair"))
        {
            if (confirming)
            {
                _confirming = null;
                unpair(pair.Id);
            }
            else
            {
                _confirming = (pair.Id, DateTime.UtcNow.AddSeconds(4));
            }
        }
    }

    /// <summary>Le bouton des animations, VFX et sons de ce pair, et son menu.</summary>
    /// <remarks>
    /// Accentué dès qu'une catégorie est bloquée : sans cela, un pair dont on
    /// a coupé les sons il y a un mois paraîtrait simplement cassé.
    /// </remarks>
    private void DrawReceive(PairRecord pair, string id)
    {
        var receive = pair.Receive;
        var limited = receive != TransientCategories.All;

        if (Btn.Icon(Icons.Effects, $"effects_{id}",
                     tone: limited ? BtnTone.Secondary : BtnTone.Ghost,
                     tooltip: limited ? "Animations, VFX et sons : certains sont bloqués pour ce pair"
                                      : "Animations, VFX et sons reçus de ce pair"))
            ImGui.OpenPopup($"effets_{id}");

        using var popup = ImRaii.Popup($"effets_{id}");

        if (popup.Success is false)
            return;

        Text.Small("Recevoir de ce pair :");

        var animations = receive.Animations;
        var vfx = receive.Vfx;
        var sounds = receive.Sounds;

        var changed = ImGui.Checkbox($"Animations##anim_{id}", ref animations);
        changed |= ImGui.Checkbox($"VFX##vfx_{id}", ref vfx);
        changed |= ImGui.Checkbox($"Sons##sons_{id}", ref sounds);

        if (changed)
            setReceive(pair.Id, new TransientCategories(animations, vfx, sounds));
    }

    /// <summary>Centre un texte d'une ligne sur la hauteur d'un bouton.</summary>
    private static void AlignToFrame()
        => ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f));

    private static Vector4 Tint(PairRecord pair, PeerStatus? status)
        => pair.Trust is PairTrust.Blocked ? Theme.Danger
         : pair.Paused ? Theme.Idle
         : status is null || status.State is PeerSessionState.Disconnected ? Theme.TextFaint
         : status.Applied ? Theme.Online
         : Theme.Accent;

    /// <summary>
    /// Direct ou par le relais, et la latence.
    /// </summary>
    /// <remarks>
    /// Discret, parce que le chemin ne change rien à ce que l'on voit. Mais il
    /// dit qui connaît l'adresse de qui, et un joueur en relais seul doit
    /// pouvoir vérifier que c'est bien le cas.
    /// </remarks>
    private static void DrawRoute(PairRecord pair, PeerRoute route)
    {
        var latency = route.RoundTripMs > 0 ? $", {route.RoundTripMs} ms" : "";

        if (route.Relayed is false)
        {
            Text.Icon(Icons.Direct, Theme.TextFaint);
            Feedback.TooltipOnHover($"Connexion directe{latency}. Ce pair connaît votre adresse IP.");
            return;
        }

        // Relayé après un perçage raté, les adresses ont déjà circulé : seul le
        // relais choisi d'emblée garde la nôtre pour nous. Le dire autrement
        // promettrait une protection qui n'a pas eu lieu.
        var privacy = pair.Policy is ConnectionPolicy.RelayOnly
            ? "Votre adresse IP n'a pas été communiquée à ce pair."
            : "La connexion directe a échoué, mais vos adresses ont été échangées pendant la tentative.";

        Text.Icon(Icons.Relayed, Theme.TextFaint);
        Feedback.TooltipOnHover(
            $"Via le relais du rendez-vous{latency}. Le service transporte sans pouvoir lire. {privacy}");
    }

    /// <summary>La puce d'état, qui résume ce que le moteur sait du pair.</summary>
    private static void DrawState(PairRecord pair, PeerStatus? status)
    {
        if (pair.Trust is PairTrust.Blocked)
        {
            Chip.Draw("bloqué", Theme.Danger, Icons.Blocked);
            return;
        }

        if (pair.Paused)
        {
            Chip.Draw("en pause", Theme.Idle, Icons.Paused);
            return;
        }

        if (status is null || status.State is PeerSessionState.Disconnected)
        {
            Chip.Draw("hors ligne", Theme.TextFaint, Icons.Waiting);
            return;
        }

        if (status.Applied)
        {
            Chip.Draw("apparence posée", Theme.Online, Icons.Applied);
            return;
        }

        var view = status.View;

        if (view.Ready)
        {
            Chip.Draw("prêt, hors de vue", Theme.Accent, Icons.Connected);
            return;
        }

        if (view.MissingBytes > 0)
        {
            var received = view.ReceivedBytes / 1024 / 1024;
            var total    = view.MissingBytes / 1024 / 1024;

            Chip.Draw($"réception {received} / {total} Mo", Theme.Idle, Icons.Receiving);
            return;
        }

        Chip.Draw("connecté", Theme.Accent, Icons.Connected);
    }
}
