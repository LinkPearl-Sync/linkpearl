using Dalamud.Bindings.ImGui;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Sync;
using Linkpearl.Integration;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Le carnet de pairs, et ce que le moteur fait avec chacun.
/// </summary>
/// <remarks>
/// <b>Aucune clé n'y est montrée.</b> L'utilisateur voit des noms, qui sont
/// l'identité qui l'intéresse : il veut voir les mods de quelqu'un qu'il a
/// devant lui, pas d'une suite hexadécimale.
/// </remarks>
internal sealed class PairsPage(PairingService pairing, Func<IReadOnlyList<PeerStatus>> statuses)
{
    public int Count => pairing.Book.All.Count;

    public void Draw()
    {
        Text.Title("Pairs");
        Text.Small("Ce que chacun vous montre, et où en est le transfert.");
        ImGui.Dummy(Theme.S(0f, Theme.GapL));

        var pairs = pairing.Book.All.ToList();

        if (pairs.Count == 0)
        {
            Feedback.EmptyState(
                Icons.Pairs,
                "Aucun pair",
                "Allez dans « Autour de vous » et demandez le pairage à quelqu'un qui utilise Linkpearl.");

            return;
        }

        var byPeer = statuses().ToDictionary(status => status.Peer);

        foreach (var pair in pairs)
        {
            byPeer.TryGetValue(pair.Id, out var status);

            using var card = Card.Begin($"pair_{pair.Id.ToHex()}", CardTone.Interactive,
                                        accent: Theme.FromName(pair.DisplayName));

            Text.H2(pair.DisplayName);
            ImGui.Dummy(Theme.S(0f, Theme.GapXs));

            DrawState(pair, status);

            if (pair.KeyVerified is false)
            {
                ImGui.SameLine(0f, Theme.S(Theme.GapS));
                Chip.Draw("non vérifié de vive voix", Theme.TextFaint, Icons.Unverified);
            }

            if (status is { FingerprintDisputed: true })
            {
                ImGui.Dummy(Theme.S(0f, Theme.GapS));

                Feedback.Alert(Theme.Danger, Icons.Warning,
                    "Ce pair annonce un autre personnage que celui auprès duquel vous vous êtes pairés. "
                  + "Rien ne lui est appliqué.");
            }

            if (status is { LastFailure: { } failure })
            {
                ImGui.Dummy(Theme.S(0f, Theme.GapXs));
                Text.Small(failure, Theme.TextFaint);
            }
        }
    }

    /// <summary>La puce d'état, qui résume tout ce que le moteur sait du pair.</summary>
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
            Chip.Draw("prêt, en attente de le voir", Theme.Accent, Icons.Connected);
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
