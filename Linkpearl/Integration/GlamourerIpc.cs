using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;

namespace Linkpearl.Integration;

/// <summary>
/// Adaptateur mince vers Glamourer.
/// </summary>
public sealed class GlamourerIpc
{
    /// <summary>
    /// Clé de verrou, constante et non nulle.
    /// </summary>
    /// <remarks>
    /// Sans clé, l'automation de Glamourer chez le receveur écrase l'état qu'on
    /// vient d'appliquer. Avec elle, l'état est verrouillé et nous seuls pouvons
    /// le relâcher, ce qui rend aussi le nettoyage fiable.
    /// </remarks>
    public const uint LockKey = 0x4C4B5031;   // « LKP1 »

    private readonly ApiVersion _version;
    private readonly GetStateBase64 _getState;
    private readonly ApplyState _applyState;
    private readonly RevertState _revertState;
    private readonly UnlockState _unlockState;

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        _version     = new ApiVersion(pi);
        _getState    = new GetStateBase64(pi);
        _applyState  = new ApplyState(pi);
        _revertState = new RevertState(pi);
        _unlockState = new UnlockState(pi);
    }

    public (int Major, int Minor)? TryGetVersion()
    {
        try
        {
            return _version.Invoke();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>État de notre propre personnage, lu sans verrou.</summary>
    public string? StateOf(int objectIndex)
    {
        var (ec, state) = _getState.Invoke(objectIndex, 0);
        return ec is GlamourerApiEc.Success ? state : null;
    }

    public void ApplyState(string base64, int objectIndex)
    {
        var ec = _applyState.Invoke(base64, objectIndex, LockKey, ApplyFlag.Once | ApplyFlag.Equipment | ApplyFlag.Customization);
        if (ec is not GlamourerApiEc.Success)
            throw new InvalidOperationException($"application d'état refusée par Glamourer : {ec}");
    }

    /// <summary>Rend le personnage à son état normal et relâche notre verrou.</summary>
    public void Release(int objectIndex)
    {
        _revertState.Invoke(objectIndex, LockKey, ApplyFlag.Equipment | ApplyFlag.Customization);
        _unlockState.Invoke(objectIndex, LockKey);
    }
}
