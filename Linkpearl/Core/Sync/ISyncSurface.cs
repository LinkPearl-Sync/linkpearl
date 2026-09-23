using Linkpearl.Core.Abstractions;
using Linkpearl.Core.Identity;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Core.Sync;

/// <summary>Ce que le moteur sait de notre propre apparence.</summary>
/// <remarks>
/// Abstrait pour que le moteur se teste sans le jeu : le harnais en fournit une
/// version qui lit un répertoire, le plugin une version qui interroge Penumbra.
/// </remarks>
public interface ILocalAppearance
{
    /// <summary>Le manifeste courant, reconstruit seulement quand quelque chose a changé.</summary>
    Task<CharacterManifest?> CurrentAsync(CancellationToken ct);

    /// <summary>Notre empreinte de personnage, ou null hors du jeu.</summary>
    PlayerFingerprint? Fingerprint { get; }
}

/// <summary>Ce que le moteur peut faire apparaître à l'écran.</summary>
public interface IRemoteApplicator
{
    /// <summary>Pose l'apparence d'un pair sur l'objet de jeu qui lui correspond.</summary>
    /// <returns>Faux si les extras n'ont pas pu être posés : le moteur les retentera seuls.</returns>
    Task<bool> ApplyAsync(PeerId peer, GameObjectRef target, CharacterManifest manifest, CancellationToken ct);

    /// <summary>
    /// Pose seulement les extras qui ont changé, sans redessin, sur un objet où
    /// l'apparence est déjà posée.
    /// </summary>
    /// <returns>Faux si le personnage n'a pas fini de se charger à temps : rien n'a été posé.</returns>
    Task<bool> ApplyExtrasAsync(PeerId peer, GameObjectRef target, CharacterExtras extras, ExtrasChange change, CancellationToken ct);

    /// <summary>Retire ce que nous avons posé pour ce pair.</summary>
    Task RemoveAsync(PeerId peer, CancellationToken ct);

    /// <summary>Vrai quand le jeu est dans un état où l'on peut appliquer.</summary>
    bool CanApply(out string reason);
}
