using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Linkpearl.Core.Crypto;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Safety;
using Linkpearl.Core.Sync;
using Linkpearl.Ui.Components;

namespace Linkpearl.Ui.Pages;

/// <summary>
/// Les groupes privés : en rejoindre un, en créer un, et gouverner ceux qu'on tient.
/// </summary>
/// <remarks>
/// La page ne fait que lire le carnet et la candidature : chaque geste passe
/// par <see cref="GroupActions"/>, qui décide du thread et du signataire. Elle
/// ne choisit donc jamais sous quelle clé une politique est signée.
///
/// <b>Aucune clé n'y est montrée</b>, pas plus que dans les pairs : un membre
/// se reconnaît à son nom. Seul le code du groupe s'affiche, et seulement à
/// ceux qui ont le droit de le distribuer.
/// </remarks>
internal sealed class GroupsPage(
    GroupBook groups, AdmissionCandidate candidate, Func<IReadOnlyList<PeerStatus>> statuses, GroupActions actions)
{
    /// <summary>
    /// La dissolution n'est relayée que par le propriétaire : l'oublier trop
    /// tôt laisserait des membres dans un groupe qu'ils croient vivant.
    /// </summary>
    private const string DissolveReminder =
        "Les membres l'apprennent en vous croisant : gardez le groupe dans la liste jusqu'à ce qu'ils l'aient vu.";

    private string _joinCode = "";
    private string _joinPassword = "";
    private string _createName = "";
    private string _createPassword = "";

    /// <summary>Le nouveau mot de passe saisi, par groupe : deux groupes ouverts ne partagent pas un champ.</summary>
    private readonly Dictionary<GroupId, string> _newPasswords = [];

    /// <summary>Le geste qui attend un second clic, et jusqu'à quand.</summary>
    /// <remarks>Une clé textuelle plutôt qu'un identifiant : exclure, quitter et dissoudre se confirment tous ainsi.</remarks>
    private (string Key, DateTime Until)? _confirming;

    public void Draw()
    {
        Text.Title("Groupes");
        Text.Small("Un groupe synchronise tous ses membres entre eux, sans les pairer un à un.");
        ImGui.Dummy(Theme.S(0f, Theme.GapM));

        DrawJoin();
        DrawCandidacy();
        DrawCreate();

        var all = groups.All;

        if (all.Count == 0)
        {
            Feedback.EmptyState(Icons.Groups, "Aucun groupe", "Créez-en un ou rejoignez-en un avec son code.");
            return;
        }

        var known = statuses();
        var ours = actions.OurIdentityKey();

        foreach (var group in all.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
            DrawGroup(group, GroupGovernance.RoleOf(group, ours), known);
    }

    private void DrawJoin()
    {
        using var card = Card.Begin("groups_join");

        Text.WithIcon(Icons.Invite, "Rejoindre", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        ImGui.SetNextItemWidth(Card.FullWidth);
        ImGui.InputTextWithHint("##group_code", "XXXX-XXXX-XXXX@service", ref _joinCode, 300);

        ImGui.SetNextItemWidth(Theme.S(260f));
        ImGui.InputTextWithHint("##group_join_password", "Mot de passe (facultatif)", ref _joinPassword,
                                GroupPolicyCodec.MaxPasswordBytes, ImGuiInputTextFlags.Password);

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        // Une candidature à la fois : en relancer une pendant qu'un membre
        // vérifie la preuve la remplacerait sous ses pieds.
        var busy = candidate.State is CandidacyState.Waiting or CandidacyState.Proving;

        if (Btn.Draw("Rejoindre", BtnTone.Primary, BtnSize.Small, Icons.Invite, id: "group_join",
                     disabled: busy || _joinCode.Trim().Length == 0))
        {
            actions.Join(_joinCode, _joinPassword);

            // Effacé dès l'envoi : un mot de passe n'a pas à rester lisible
            // dans un champ pendant qu'on joue, fenêtre ouverte.
            _joinPassword = "";
        }
    }

    /// <summary>Où en est la candidature, juste sous la carte qui l'a lancée.</summary>
    private void DrawCandidacy()
    {
        switch (candidate.State)
        {
            case CandidacyState.Waiting:
                Text.Small(
                    "Demande envoyée. En attente d'un membre en ligne, ou d'un modérateur si le groupe valide "
                  + "chaque entrée.", Theme.Accent);

                if (Btn.Draw("Annuler", BtnTone.Ghost, BtnSize.Small, Icons.Decline, id: "group_join_cancel"))
                    actions.CancelJoin();

                break;

            case CandidacyState.NeedsPassword:
                Text.Small("Ce groupe demande un mot de passe. Saisissez-le et relancez.", Theme.Idle);
                break;

            case CandidacyState.Proving:
                Text.Small("Mot de passe envoyé…", Theme.Accent);
                break;

            case CandidacyState.Refused:
                Text.Small(candidate.RefusalReason switch
                {
                    RefusalReason.WrongPassword   => "Mot de passe incorrect.",
                    RefusalReason.TooManyAttempts => "Trop d'essais : réessayez dans une demi-heure.",
                    _                             => "Un modérateur a refusé votre demande.",
                }, Theme.Danger);
                break;

            case CandidacyState.Expired:
                Text.Small("Personne n'a répondu en dix minutes. Un membre doit être en ligne.", Theme.Idle);
                break;

            case CandidacyState.Idle when candidate.LastJoinedName is { } name:
                Text.Small($"Vous avez rejoint {Glyphs.Safe(name)}.", Theme.Online);
                break;
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapS));
    }

    private void DrawCreate()
    {
        using var card = Card.Begin("groups_create");

        Text.WithIcon(Icons.Groups, "Créer un groupe", Theme.Accent);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        // Le tampon en octets, la limite en caractères : un nom accentué de
        // trente-deux caractères dépasse trente-deux octets, et c'est
        // IsValidName qui tranche, comme le fera chaque membre en le recevant.
        ImGui.SetNextItemWidth(Theme.S(260f));
        ImGui.InputTextWithHint("##group_name", "Nom du groupe (32 caractères)", ref _createName,
                                GroupPolicyCodec.MaxNameBytes);

        ImGui.SetNextItemWidth(Theme.S(260f));
        ImGui.InputTextWithHint("##group_create_password", "Mot de passe (facultatif)", ref _createPassword,
                                GroupPolicyCodec.MaxPasswordBytes, ImGuiInputTextFlags.Password);
        Feedback.Hint("Sans mot de passe, chaque entrée devra être validée par vous ou un modérateur.");

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (Btn.Draw("Créer", BtnTone.Secondary, BtnSize.Small, Icons.Accept, id: "group_create",
                     disabled: GroupPolicyCodec.IsValidName(_createName.Trim()) is false))
        {
            actions.Create(_createName.Trim(), _createPassword);
            _createName = "";
            _createPassword = "";
        }
    }

    private void DrawGroup(GroupRecord group, GroupRole role, IReadOnlyList<PeerStatus> known)
    {
        var id = group.Id.ToString();
        var policy = group.Policy;
        var dissolved = policy is { Dissolved: true };

        using (ImRaii.PushColor(ImGuiCol.Header, Theme.BgSurface)
                     .Push(ImGuiCol.HeaderHovered, Theme.BgRaised)
                     .Push(ImGuiCol.HeaderActive, Theme.BgRaised))
        {
            if (ImGui.CollapsingHeader($"{Glyphs.Safe(group.Name)} ({group.Members.Count})##group_{id}",
                                       ImGuiTreeNodeFlags.DefaultOpen) is false)
                return;
        }

        using var scope = ImRaii.PushId(id);

        DrawChips(role, policy);

        // Sans politique, un modérateur n'est pas encore reconnu comme tel :
        // RoleOf rend alors « membre », et rien de ce qui suit n'est proposé.
        if (role is not GroupRole.Member && policy is not null && dissolved is false
            && GroupGovernance.CodeOf(group) is { } code)
            DrawCode(group, code);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        DrawMembers(group, role, known);

        if (role is not GroupRole.Member && policy is not null && dissolved is false)
            DrawManagement(group, role, policy);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (dissolved)
        {
            if (Btn.Draw("Retirer de la liste", BtnTone.Ghost, BtnSize.Small, Icons.Remove, id: "forget"))
                actions.Forget(group.Id);

            if (role is GroupRole.Owner)
                Feedback.Hint(DissolveReminder);
        }
        else if (role is not GroupRole.Owner)
        {
            // Le propriétaire ne quitte pas : il dissout, depuis la gestion.
            if (Confirmed($"leave_{id}", "Quitter le groupe", Icons.Leave,
                          "Cliquer encore pour quitter ce groupe", tooltip: null))
                actions.Leave(group.Id);
        }

        ImGui.Dummy(Theme.S(0f, Theme.GapM));
    }

    private static void DrawChips(GroupRole role, GroupPolicy? policy)
    {
        var (label, icon) = role switch
        {
            GroupRole.Owner     => ("propriétaire", Icons.Verified),
            GroupRole.Moderator => ("modérateur", Icons.Moderator),
            _                   => ("membre", Icons.Character),
        };

        Chip.Draw(label, Theme.Accent, icon);

        if (policy is { Dissolved: true })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Chip.Draw("dissous", Theme.Danger, Icons.Blocked);
        }

        if (policy is null)
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Chip.Draw("en attente de sa politique", Theme.Idle, Icons.Waiting);
        }
    }

    /// <summary>Le code à distribuer, montré à ceux qui peuvent le changer.</summary>
    private static void DrawCode(GroupRecord group, InvitationTicket code)
    {
        var services = group.Policy is { Rendezvous.Count: > 0 } policy ? policy.Rendezvous : group.Rendezvous;

        if (services.Count == 0)
            return;

        var service = services[0];
        var text = InvitationTicketText.Encode(code, service);

        // Par groupes de quatre, comme on le dicte : le ticket ignore ses
        // propres tirets à la lecture, donc la forme copiée reste valable.
        var ticket = text[..InvitationTicket.Length];
        var shown = $"{ticket[..4]}-{ticket[4..8]}-{ticket[8..]}{text[InvitationTicket.Length..]}";

        ImGui.Dummy(Theme.S(0f, Theme.GapXs));
        ImGui.AlignTextToFramePadding();

        // L'adresse du service peut venir de la politique, donc du réseau.
        Text.Body(Glyphs.Safe(shown), Theme.TextMuted);
        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        if (Btn.Icon(Icons.Copy, "copy_code", tooltip: "Copier le code"))
            ImGui.SetClipboardText(shown);
    }

    private void DrawMembers(GroupRecord group, GroupRole role, IReadOnlyList<PeerStatus> known)
    {
        if (group.Members.Count == 0)
        {
            Text.Small("Aucun membre rencontré pour l'instant.", Theme.TextFaint);
            return;
        }

        using var table = ImRaii.Table("members", 4, ImGuiTableFlags.NoBordersInBody | ImGuiTableFlags.PadOuterX);

        if (table.Success is false)
            return;

        ImGui.TableSetupColumn("état", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("nom", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("statut", ImGuiTableColumnFlags.WidthFixed, Theme.S(190f));
        ImGui.TableSetupColumn("actions", ImGuiTableColumnFlags.WidthFixed, (ImGui.GetFrameHeight() * 4f) + Theme.S(Theme.GapS * 3f));

        foreach (var member in group.Members.Values.OrderBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            // Par groupe et par personnage, pas par clé : la clé d'un membre
            // n'est connue qu'après le premier handshake.
            var status = known.FirstOrDefault(
                status => status.Group == group.Id && status.View.Fingerprint == member.Fingerprint);

            MemberRow(group, role, member, status);
        }
    }

    private void MemberRow(GroupRecord group, GroupRole role, GroupMember member, PeerStatus? status)
    {
        var id = $"{member.Fingerprint.High:x16}{member.Fingerprint.Low:x16}";
        var policy = group.Policy;
        var banned = policy?.IsBanned(member.Id, member.Fingerprint) is true;
        var moderator = member.PublicKey is { } key && policy?.IsModerator(CryptoPrimitives.Compress(key)) is true;

        ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight() + Theme.S(Theme.GapS));

        ImGui.TableNextColumn();
        AlignToFrame();
        Feedback.StatusDot(Tint(member, banned, status));

        ImGui.TableNextColumn();
        AlignToFrame();
        ImGui.TextColored(Theme.Text, Glyphs.Safe(member.DisplayName));

        if (moderator)
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Text.Icon(Icons.Moderator, Theme.TextFaint);
            Feedback.TooltipOnHover("Modérateur");
        }

        if (status is { LastFailure: { } failure })
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            Text.Icon(Icons.Warning, Theme.Idle);
            Feedback.TooltipOnHover(failure);
        }

        ImGui.TableNextColumn();
        AlignToFrame();
        DrawState(member, banned, status);

        ImGui.TableNextColumn();

        if (member.Paused)
        {
            if (Btn.Icon(Icons.Resume, $"resume_{id}", tooltip: "Reprendre"))
                actions.SetPaused(group.Id, member.Fingerprint, false);
        }
        else if (Btn.Icon(Icons.Paused, $"pause_{id}", tooltip: "Mettre en pause : la session se ferme et l'apparence est retirée"))
        {
            actions.SetPaused(group.Id, member.Fingerprint, true);
        }

        ImGui.SameLine(0f, Theme.S(Theme.GapS));
        DrawReceive(group, member, id);

        if (policy is null || policy.Dissolved)
            return;

        if (role is GroupRole.Owner)
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            DrawModeratorToggle(group, member, moderator, id);
        }

        if (role is not GroupRole.Member && banned is false)
        {
            ImGui.SameLine(0f, Theme.S(Theme.GapS));
            DrawExclude(group, role, member, id);
        }
    }

    /// <summary>Nommer ou démettre un modérateur. Au propriétaire seul.</summary>
    private void DrawModeratorToggle(GroupRecord group, GroupMember member, bool moderator, string id)
    {
        // La politique porte des clés, pas des personnages : un membre dont on
        // ne connaît que le personnage ne peut pas être nommé.
        if (member.PublicKey is not { } key)
        {
            Btn.Icon(Icons.Moderator, $"moderator_{id}", disabled: true,
                     tooltip: "Il faut l'avoir croisé une fois pour connaître sa clé.");
            return;
        }

        if (Btn.Icon(Icons.Moderator, $"moderator_{id}",
                     tone: moderator ? BtnTone.Secondary : BtnTone.Ghost,
                     tooltip: moderator ? "Retirer ce modérateur" : "Nommer modérateur"))
            actions.Edit(group.Id, (current, _) => GroupGovernance.SetModerators(current, Toggled(current, key)));
    }

    /// <summary>La liste des modérateurs, avec ou sans cette clé.</summary>
    /// <remarks>
    /// Recalculée sur le groupe que le plugin tient au moment de signer, pas
    /// sur celui qu'on a dessiné : une politique arrivée entre-temps ne perd
    /// ainsi pas un modérateur nommé ailleurs.
    /// </remarks>
    private static List<byte[]> Toggled(GroupRecord group, byte[] key)
    {
        var target = PeerId.Of(key);
        var moderators = new List<byte[]>();
        var present = false;

        foreach (var compressed in group.Policy?.Attestation.Moderators ?? [])
        {
            byte[] full;

            try
            {
                full = CryptoPrimitives.Decompress(compressed);
            }
            catch (CryptographicException)
            {
                // Une politique acceptée n'en porte pas, mais le plugin ne
                // rattrape que ce type-là : une exception d'un autre type
                // remonterait jusqu'au dessin de la fenêtre.
                throw new InvalidOperationException("clé de modérateur illisible");
            }

            if (PeerId.Of(full) == target)
                present = true;
            else
                moderators.Add(full);
        }

        if (present is false)
            moderators.Add(key);

        return moderators;
    }

    /// <summary>Exclure : bannir la clé et le personnage, en deux clics.</summary>
    private void DrawExclude(GroupRecord group, GroupRole role, GroupMember member, string id)
    {
        // Les règles refusent qu'un modérateur vise le propriétaire ou un
        // autre modérateur : autant ne pas proposer le geste.
        if (role is GroupRole.Moderator && member.Id is { } peer && group.Policy!.IsProtected(peer))
        {
            Btn.Icon(Icons.Blocked, $"exclude_{id}", disabled: true,
                     tooltip: "Seul le propriétaire peut exclure un modérateur.");
            return;
        }

        var key = $"exclude_{group.Id}_{id}";
        var confirming = IsConfirming(key);

        if (Btn.Icon(Icons.Blocked, $"exclude_{id}",
                     tone: confirming ? BtnTone.Danger : BtnTone.Ghost,
                     tooltip: confirming ? "Cliquer encore pour exclure ce membre" : "Exclure"))
        {
            if (confirming)
            {
                _confirming = null;

                // La clé et le personnage : l'une seule laisserait revenir la
                // même personne sous l'autre.
                var ban = new GroupBan(member.Id, member.Fingerprint);
                actions.Edit(group.Id, (current, signer) => GroupGovernance.Ban(current, ban, signer));
            }
            else
            {
                _confirming = (key, DateTime.UtcNow.AddSeconds(4));
            }
        }
    }

    /// <summary>Le bouton des animations, VFX et sons de ce membre, et son menu.</summary>
    /// <remarks>Copie de celui des pairs : accentué dès qu'une catégorie est bloquée.</remarks>
    private void DrawReceive(GroupRecord group, GroupMember member, string id)
    {
        var receive = member.Receive;
        var limited = receive != TransientCategories.All;

        if (Btn.Icon(Icons.Effects, $"effects_{id}",
                     tone: limited ? BtnTone.Secondary : BtnTone.Ghost,
                     tooltip: limited ? "Animations, VFX et sons : certains sont bloqués pour ce membre"
                                      : "Animations, VFX et sons reçus de ce membre"))
            ImGui.OpenPopup($"effets_{id}");

        using var popup = ImRaii.Popup($"effets_{id}");

        if (popup.Success is false)
            return;

        Text.Small("Recevoir de ce membre :");

        var animations = receive.Animations;
        var vfx = receive.Vfx;
        var sounds = receive.Sounds;

        var changed = ImGui.Checkbox($"Animations##anim_{id}", ref animations);
        changed |= ImGui.Checkbox($"VFX##vfx_{id}", ref vfx);
        changed |= ImGui.Checkbox($"Sons##sons_{id}", ref sounds);

        if (changed)
            actions.SetReceive(group.Id, member.Fingerprint, new TransientCategories(animations, vfx, sounds));
    }

    /// <summary>La gestion, repliée : on y vient rarement, et rien ne doit s'y toucher par mégarde.</summary>
    private void DrawManagement(GroupRecord group, GroupRole role, GroupPolicy policy)
    {
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (ImGui.TreeNodeEx("Gestion##management") is false)
            return;

        DrawPassword(group, policy);
        ImGui.Dummy(Theme.S(0f, Theme.GapS));

        if (Btn.Draw("Nouveau code", BtnTone.Secondary, BtnSize.Small, Icons.Refresh, id: "new_code",
                     tooltip: "L'ancien code ne mène plus nulle part. Les membres restent ; seuls ceux qui ne sont "
                            + "pas encore entrés devront recevoir le nouveau."))
            actions.Edit(group.Id, GroupGovernance.NewCode);

        ImGui.Dummy(Theme.S(0f, Theme.GapS));
        DrawBans(group, policy);

        if (role is GroupRole.Owner)
        {
            ImGui.Dummy(Theme.S(0f, Theme.GapS));
            DrawAdmissionMode(group, policy);

            ImGui.Dummy(Theme.S(0f, Theme.GapS));

            // Pour le propriétaire, quitter c'est dissoudre : voir LeaveGroup
            // dans le plugin.
            if (Confirmed($"dissolve_{group.Id}", "Dissoudre", Icons.Remove,
                          "Cliquer encore pour dissoudre ce groupe", DissolveReminder))
                actions.Leave(group.Id);
        }

        ImGui.TreePop();
    }

    private void DrawPassword(GroupRecord group, GroupPolicy policy)
    {
        var password = _newPasswords.GetValueOrDefault(group.Id, "");

        ImGui.SetNextItemWidth(Theme.S(220f));

        if (ImGui.InputTextWithHint("##new_password", "Nouveau mot de passe", ref password,
                                    GroupPolicyCodec.MaxPasswordBytes, ImGuiInputTextFlags.Password))
            _newPasswords[group.Id] = password;

        ImGui.SameLine(0f, Theme.S(Theme.GapS));

        // Vide, il serait refusé en mode mot de passe : les règles
        // n'acceptent pas un groupe où personne ne pourrait plus entrer.
        if (Btn.Draw("Changer le mot de passe", BtnTone.Secondary, BtnSize.Small, Icons.Lock, id: "set_password",
                     disabled: password.Length == 0))
        {
            actions.Edit(group.Id, (current, signer) => GroupGovernance.SetPassword(current, password, signer));

            // Effacé dès l'envoi, comme à l'entrée : il n'a plus rien à faire
            // dans un champ.
            _newPasswords.Remove(group.Id);
        }

        if (policy.Attestation.Admission == AdmissionMode.Validation)
            Feedback.Hint("Tant que le groupe valide chaque entrée, le mot de passe ne fait entrer personne.");
    }

    private void DrawBans(GroupRecord group, GroupPolicy policy)
    {
        Text.Small($"Exclus ({policy.Bans.Count})", Theme.TextMuted);

        if (policy.Bans.Count == 0)
        {
            Text.Small("Personne.", Theme.TextFaint);
            return;
        }

        for (var index = 0; index < policy.Bans.Count; index++)
        {
            var ban = policy.Bans[index];

            using var scope = ImRaii.PushId(index);

            ImGui.AlignTextToFramePadding();
            Text.Body(Glyphs.Safe(NameOf(group, ban)));
            ImGui.SameLine(0f, Theme.S(Theme.GapS));

            if (Btn.Draw("Lever", BtnTone.Ghost, BtnSize.Small, Icons.Accept, id: "unban"))
                actions.Edit(group.Id, (current, signer) => GroupGovernance.Unban(current, ban, signer));
        }
    }

    /// <summary>Le nom d'un exclu, si on l'a croisé ; sinon rien qui ressemble à une clé.</summary>
    private static string NameOf(GroupRecord group, GroupBan ban)
    {
        if (ban.Fingerprint is { } fingerprint && group.Members.TryGetValue(fingerprint, out var byCharacter))
            return byCharacter.DisplayName;

        if (ban.Peer is { } peer && group.Members.Values.FirstOrDefault(member => member.Id == peer) is { } byKey)
            return byKey.DisplayName;

        return "personnage jamais croisé";
    }

    private void DrawAdmissionMode(GroupRecord group, GroupPolicy policy)
    {
        var mode = policy.Attestation.Admission;

        Text.Small("Mode d'admission", Theme.TextMuted);

        // Les règles refusent le mode mot de passe sans mot de passe : ce
        // serait un groupe où personne ne pourrait plus entrer.
        var noPassword = policy.Password.Length == 0;

        using (ImRaii.Disabled(noPassword && mode != AdmissionMode.Password))
        {
            if (ImGui.RadioButton("Mot de passe##mode_password", mode == AdmissionMode.Password)
                && mode != AdmissionMode.Password)
                actions.Edit(group.Id, (current, _) => GroupGovernance.SetAdmission(current, AdmissionMode.Password));
        }

        if (noPassword)
            Feedback.TooltipOnHover("Définissez d'abord un mot de passe.");

        ImGui.SameLine(0f, Theme.S(Theme.GapM));

        if (ImGui.RadioButton("Validation par un modérateur##mode_validation", mode == AdmissionMode.Validation)
            && mode != AdmissionMode.Validation)
            actions.Edit(group.Id, (current, _) => GroupGovernance.SetAdmission(current, AdmissionMode.Validation));
    }

    /// <summary>Un bouton qui n'agit qu'au second clic dans les quatre secondes, comme le retrait d'un pair.</summary>
    private bool Confirmed(string key, string label, FontAwesomeIcon icon, string confirmTooltip, string? tooltip)
    {
        var confirming = IsConfirming(key);

        if (Btn.Draw(label, confirming ? BtnTone.Danger : BtnTone.Ghost, BtnSize.Small, icon, id: key,
                     tooltip: confirming ? confirmTooltip : tooltip) is false)
            return false;

        if (confirming)
        {
            _confirming = null;
            return true;
        }

        _confirming = (key, DateTime.UtcNow.AddSeconds(4));
        return false;
    }

    private bool IsConfirming(string key) => _confirming is { } c && c.Key == key && c.Until > DateTime.UtcNow;

    /// <summary>Centre un texte d'une ligne sur la hauteur d'un bouton.</summary>
    private static void AlignToFrame()
        => ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) * 0.5f));

    private static Vector4 Tint(GroupMember member, bool banned, PeerStatus? status)
        => banned ? Theme.Danger
         : member.Paused ? Theme.Idle
         : status is null || status.State is PeerSessionState.Disconnected ? Theme.TextFaint
         : status.Applied ? Theme.Online
         : Theme.Accent;

    /// <summary>La puce d'état, même logique que celle des pairs, plus l'exclusion.</summary>
    private static void DrawState(GroupMember member, bool banned, PeerStatus? status)
    {
        if (banned)
        {
            Chip.Draw("exclu", Theme.Danger, Icons.Blocked);
            return;
        }

        if (member.Paused)
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
