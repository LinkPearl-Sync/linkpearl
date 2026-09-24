using System.Security.Cryptography;
using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Groups;
using Linkpearl.Core.Safety;

namespace Linkpearl.Ui;

/// <summary>
/// Ce que la page Groupes peut demander au plugin.
/// </summary>
/// <remarks>
/// Des délégués plutôt qu'une référence au plugin : la page ne voit ainsi ni la
/// présence, ni l'identité, ni le carnet en écriture, et chaque geste passe par
/// un seul endroit qui décide du thread, du signataire et de ce qu'on dit au
/// joueur. Tous s'appellent depuis le thread du jeu, là où l'interface dessine.
/// </remarks>
public sealed class GroupActions
{
    /// <summary>Crée un groupe (nom, mot de passe ; vide pour la validation par modérateur).</summary>
    public required Action<string, string> Create { get; init; }

    /// <summary>Demande à rejoindre un groupe (texte du code collé, mot de passe).</summary>
    public required Action<string, string> Join { get; init; }

    /// <summary>Abandonne la candidature en cours.</summary>
    public required Action CancelJoin { get; init; }

    /// <summary>Quitte un groupe ; pour son propriétaire, le dissout.</summary>
    public required Action<GroupId> Leave { get; init; }

    /// <summary>Retire de la liste un groupe dissous.</summary>
    public required Action<GroupId> Forget { get; init; }

    /// <summary>
    /// Applique une modification de gouvernance.
    /// </summary>
    /// <remarks>
    /// La fonction reçoit le groupe et le signataire : <c>null</c> pour signer
    /// comme propriétaire, notre clé d'identité pour signer comme modérateur.
    /// C'est le plugin qui choisit, d'après notre rôle : la page ne choisit
    /// jamais le signataire.
    /// </remarks>
    public required Action<GroupId, Func<GroupRecord, ECDsa?, byte[]>> Edit { get; init; }

    public required Action<PendingValidation> Approve { get; init; }

    public required Action<PendingValidation> Decline { get; init; }

    public required Action<GroupId, PlayerFingerprint, bool> SetPaused { get; init; }

    public required Action<GroupId, PlayerFingerprint, TransientCategories> SetReceive { get; init; }

    /// <summary>Notre clé d'identité publique, point de 65 octets, pour calculer notre rôle.</summary>
    public required Func<byte[]?> OurIdentityKey { get; init; }
}
