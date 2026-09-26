using Dalamud.Bindings.ImGui;
using Linkpearl.Core.Cache;
using Linkpearl.Core.Sync;
using Linkpearl.Core.Transport.Rendezvous;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>Ce qu'un annuaire a proposé, en attente du choix de l'utilisateur.</summary>
/// <remarks>
/// Rien n'est ajouté tant qu'une case n'est pas cochée. C'est ce qui empêche un
/// annuaire de devenir une autorité : il propose, l'utilisateur dispose.
/// </remarks>
public sealed class DiscoveryState
{
    public RendezvousAddress? From { get; set; }

    public IReadOnlyList<DirectoryEntry> Offered { get; set; } = [];

    public HashSet<string> Chosen { get; } = [];

    public string? Failure { get; set; }

    public bool Running { get; set; }

    public void Reset()
    {
        From = null;
        Offered = [];
        Chosen.Clear();
        Failure = null;
    }
}

/// <summary>
/// Les réglages, et surtout ce qu'ils coûtent.
/// </summary>
/// <remarks>
/// Chaque réglage est suivi de sa conséquence, pas de sa description : ce qui
/// se paie en vie privée doit se lire avant d'être coché, pas après.
/// </remarks>
internal sealed class SettingsPage(
    Configuration configuration, DiscoveryState discovery, Action<RendezvousAddress> discover, BackupCard backup,
    Action<bool> setUploadLimited, CacheChooser cacheChooser, CacheKeeper cacheKeeper, Action showOnboarding)
{
    private string _newAddress = "";

    /// <summary>Vrai pendant la suppression de l'ancien cache : le bouton se désactive, un clic répété ne lance pas une deuxième suppression concurrente.</summary>
    private volatile bool _deletingPrevious;

    /// <summary>L'échec de la dernière suppression de l'ancien cache, montré sous le bouton.</summary>
    private volatile string? _deletePreviousError;

    public void Draw()
    {
        Text.PageHeader("Réglages");

        // La sauvegarde en tête : c'est la seule carte dont l'oubli coûte
        // cher, et une réinstallation n'attend pas qu'on ait fait défiler.
        DrawIdentity();
        DrawVisibility();
        DrawCache();
        DrawNetwork();
        DrawDiscovery();
    }

    /// <summary>Visibilité : ce qui se voit de vous, et ce que ça coûte en vie privée.</summary>
    private void DrawVisibility()
    {
        using var card = Card.Begin("settings_visibility");

        Text.WithIcon(Icons.Discoverable, "Visibilité", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var discoverable = configuration.Discoverable;

        if (ImGui.Checkbox("Me signaler aux autres joueurs##discoverable", ref discoverable))
        {
            configuration.Discoverable = discoverable;
            configuration.Save();
        }

        Feedback.Hint(
            "Sans cela, personne ne peut vous reconnaître ni vous adresser une demande. Avec, "
          + "l'opérateur de chaque service peut savoir que votre personnage est en ligne : pour "
          + "qu'un inconnu puisse vous reconnaître, il faut bien que quelque chose soit calculable "
          + "à partir de votre nom.");

        // Exception voulue à la règle « tout en infobulle » : ce qui se paie
        // en vie privée se lit avant de cocher, pas au survol d'une icône.
        Text.Small("Le service sait alors que vous êtes en ligne.", Theme.TextFaint);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var glyphs = configuration.ShowNameplateGlyphs;

        if (ImGui.Checkbox("Glyphe à côté du nom##nameplate_glyphs", ref glyphs))
        {
            configuration.ShowNameplateGlyphs = glyphs;
            configuration.Save();
        }

        Feedback.Hint(NameplateLegend.Draw);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var badges = configuration.ShowTransferBadges;

        if (ImGui.Checkbox("Badges de transfert##transfer_badges", ref badges))
        {
            configuration.ShowTransferBadges = badges;
            configuration.Save();
        }

        Feedback.Hint(
            "Un badge aux pieds d'un pair visible dont l'apparence n'est pas encore là : connexion, "
          + "attente, réception avec sa progression, application. Il disparaît dès qu'elle est posée.");
    }

    /// <summary>Cache : où les apparences reçues vivent sur le disque, et combien.</summary>
    private void DrawCache()
    {
        using var card = Card.Begin("settings_cache");

        Text.WithIcon(Icons.Cache, "Cache", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        cacheChooser.Draw();

        if (cacheKeeper.PreviousRoot is not { } previous)
            return;

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        var size = cacheKeeper.PreviousBytes is { } bytes ? CacheChooser.Format(bytes) : "une taille inconnue";
        Text.Small($"L'ancien cache occupe {size} dans {previous}.", Theme.TextMuted);

        if (Btn.Draw("Supprimer l'ancien cache", BtnTone.Danger, BtnSize.Small, Icons.Remove, id: "cache_delete_previous",
                     disabled: _deletingPrevious,
                     tooltip: "N'efface que les fichiers de Linkpearl. Tout autre fichier de ce dossier reste."))
        {
            // Le drapeau désactive le bouton tout de suite : sans lui, un
            // second clic pendant la suppression lancerait une deuxième
            // suppression concurrente sur le même dossier.
            _deletingPrevious = true;
            _deletePreviousError = null;

            _ = Task.Run(() =>
            {
                try
                {
                    cacheKeeper.DeletePrevious();
                }
                catch (Exception e)
                {
                    // Montré, pas seulement journalisé : le bouton et la
                    // taille resteraient sinon affichés comme si de rien
                    // n'était, alors que rien n'a été supprimé. Le journal ne
                    // garde que le type d'exception ; l'interface, elle, peut
                    // afficher le message complet.
                    Plugin.Log.Warning($"Suppression de l'ancien cache en échec ({e.GetType().Name}).");
                    _deletePreviousError = $"échec de la suppression : {e.Message}";
                }
                finally
                {
                    _deletingPrevious = false;
                }
            });
        }

        if (_deletePreviousError is { } error)
            Text.Small(error, Theme.Danger);
    }

    /// <summary>Réseau : débit et services de rendez-vous.</summary>
    private void DrawNetwork()
    {
        using var card = Card.Begin("settings_network");

        Text.WithIcon(Icons.Rendezvous, "Réseau", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        var limited = configuration.LimitUpload;

        if (ImGui.Checkbox("Brider l'envoi##limit_upload", ref limited))
            setUploadLimited(limited);

        Feedback.Hint(
            "Bridé, l'envoi démarre lentement et recule dès que le ping gonfle : une tenue met des "
          + "minutes à arriver, mais le jeu reste fluide en donjon. Libre, elle arrive en quelques "
          + "secondes, au prix d'un ping plus haut pendant le transfert.");

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        // Aligné comme dans DrawService : sans ça, le texte flotte au-dessus
        // de l'icône ⓘ qui suit, calée sur la hauteur d'un cadre.
        ImGui.AlignTextToFramePadding();
        Text.Body("Services de rendez-vous");
        Feedback.Hint(
            "Ils aident deux joueurs à se trouver et relaient quand la connexion directe échoue. "
          + "Ils ne voient ni vos fichiers, ni vos apparences, ni vos clés. Vous ne verrez que les "
          + "joueurs avec qui vous partagez au moins un service.");

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        for (var i = 0; i < configuration.Rendezvous.Count; i++)
            DrawService(i);

        if (configuration.Rendezvous.Count == 0)
            Text.Small("Aucun service. Personne ne peut vous voir, et vous ne voyez personne.", Theme.Danger);

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        ImGui.SetNextItemWidth(Card.FullWidth - Theme.S(110f) - Feedback.HintWidth);
        ImGui.InputTextWithHint("##nouveau", "rdv.exemple.ch ou rdv.exemple.ch:443", ref _newAddress, 260);
        ImGui.SameLine();

        if (Btn.Draw("Ajouter", BtnTone.Secondary, BtnSize.Small, Icons.Invite, id: "add_rdv"))
            Add(_newAddress);

        Feedback.Hint(
            "Chaque service activé apprend que votre personnage est en ligne et qui se tient autour "
          + "de vous. En ajouter augmente vos chances de voir du monde, et le nombre de personnes "
          + "qui le savent.");
    }

    /// <summary>Identité : sauvegarde du personnage et de son carnet, et la présentation.</summary>
    private void DrawIdentity()
    {
        using var card = Card.Begin("settings_identity");

        Text.WithIcon(Icons.Backup, "Sauvegarde", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        backup.Draw();

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        if (Btn.Draw("Revoir la présentation", BtnTone.Ghost, BtnSize.Small, Icons.Info, id: "settings_onboarding"))
            showOnboarding();
    }

    private void DrawService(int index)
    {
        var entry = configuration.Rendezvous[index];

        using var id = Dalamud.Interface.Utility.Raii.ImRaii.PushId(index);

        var enabled = entry.Enabled;

        if (ImGui.Checkbox("##actif", ref enabled))
        {
            configuration.Rendezvous[index] = entry with { Enabled = enabled };
            configuration.Save();
        }

        ImGui.SameLine();

        // L'adresse et le libellé peuvent venir d'un annuaire, donc du réseau.
        var shown = entry.Label.Length > 0
            ? $"{Glyphs.Safe(entry.Label)}  ({Glyphs.Safe(entry.Address.ToString())})"
            : Glyphs.Safe(entry.Address.ToString());

        // Le texte se cale sur la hauteur des cadres, sans quoi il flotte au
        // dessus de la ligne que forment la case et les boutons.
        ImGui.AlignTextToFramePadding();
        Text.Body(shown, enabled ? Theme.Text : Theme.TextFaint);

        // Deux boutons carrés et l'espace qui les sépare : une largeur fixe
        // ne suivait ni l'échelle ni l'espacement, et poussait la corbeille
        // contre le bord.
        var buttons = ImGui.GetFrameHeight() * 2f + ImGui.GetStyle().ItemSpacing.X;

        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttons);

        if (Btn.Icon(Icons.Refresh, "discover", tooltip: "Demander à ce service ceux qu'il connaît"))
            discover(entry.Address);

        ImGui.SameLine();

        if (Btn.Icon(Icons.Remove, "remove", BtnTone.Danger, "Retirer ce service"))
        {
            configuration.Rendezvous.RemoveAt(index);
            configuration.Save();
        }
    }

    private void DrawDiscovery()
    {
        if (discovery.Running is false && discovery.From is null && discovery.Failure is null)
            return;

        using var card = Card.Begin("settings_discovery", accent: Theme.Accent);

        Text.WithIcon(Icons.Nearby, $"Services connus de {Glyphs.Safe(discovery.From?.ToString() ?? "")}",
                      Theme.Accent);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (discovery.Running)
        {
            Text.Muted("Interrogation en cours...");
            return;
        }

        if (discovery.Failure is { } failure)
        {
            Text.Small(failure, Theme.Danger);
            return;
        }

        if (discovery.Offered.Count == 0)
        {
            Text.Small("Ce service ne connaît personne, ou n'en publie aucun.", Theme.TextFaint);
        }

        foreach (var offered in discovery.Offered)
        {
            var known = configuration.Rendezvous.Any(e => e.Address.ToString() == offered.Address);
            var chosen = discovery.Chosen.Contains(offered.Address);

            using var id = Dalamud.Interface.Utility.Raii.ImRaii.PushId(offered.Address);
            using var disabled = Dalamud.Interface.Utility.Raii.ImRaii.Disabled(known);

            if (ImGui.Checkbox("##choisi", ref chosen))
            {
                if (chosen)
                    discovery.Chosen.Add(offered.Address);
                else
                    discovery.Chosen.Remove(offered.Address);
            }

            ImGui.SameLine();

            Text.Body(offered.Label.Length > 0
                ? $"{Glyphs.Safe(offered.Label)}  ({Glyphs.Safe(offered.Address)})"
                : Glyphs.Safe(offered.Address));

            if (known)
            {
                ImGui.SameLine();
                Text.Small("déjà dans votre liste", Theme.TextFaint);
            }
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        if (Btn.Draw($"Ajouter les {discovery.Chosen.Count} cochés", BtnTone.Action, BtnSize.Small,
                     Icons.Accept, disabled: discovery.Chosen.Count == 0, id: "add_chosen"))
        {
            foreach (var address in discovery.Chosen)
                Add(address, discovery.Offered.FirstOrDefault(o => o.Address == address)?.Label ?? "");

            discovery.Reset();
        }

        ImGui.SameLine();

        if (Btn.Draw("Fermer", BtnTone.Ghost, BtnSize.Small, id: "close_discovery"))
            discovery.Reset();
    }

    /// <summary>Ajoute un service, en refusant ce qui ne se lit pas.</summary>
    private void Add(string text, string label = "")
    {
        if (RendezvousAddress.TryParse(text, out var address, out var why) is false)
        {
            // La raison s'affiche telle quelle : elle est écrite pour être lue.
            discovery.Failure = $"adresse refusée : {why}";
            return;
        }

        if (configuration.Rendezvous.Any(entry => entry.Address == address))
            return;

        configuration.Rendezvous.Add(new RendezvousEntry(address, label, true));
        configuration.Save();

        _newAddress = "";
    }
}
