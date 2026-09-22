namespace Linkpearl.Core.Abstractions;

/// <summary>
/// Référence à un objet de jeu.
/// </summary>
/// <remarks>
/// L'index change au rechargement de zone, d'où l'identifiant stable qui
/// l'accompagne : appliquer une apparence sur un index périmé la poserait sur
/// quelqu'un d'autre.
/// </remarks>
public readonly record struct GameObjectRef(int ObjectIndex, ulong StableId);

/// <summary>Un joueur visible, vu par l'adaptateur Dalamud.</summary>
public sealed record VisiblePlayer(GameObjectRef Object, PlayerFingerprint Fingerprint);
