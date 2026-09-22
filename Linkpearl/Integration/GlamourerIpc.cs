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
    /// Notre clé, constante et non nulle.
    /// </summary>
    /// <remarks>
    /// C'est le drapeau <c>Lock</c> qui verrouille un état, pas la clé : passée
    /// seule, elle sert à déverrouiller si besoin. On ne verrouille pas en
    /// jalon 1, où l'on s'applique à soi-même et où un verrou empêcherait
    /// l'utilisateur de reprendre la main depuis l'interface de Glamourer.
    /// Le verrou deviendra nécessaire quand on appliquera l'apparence d'un
    /// pair, pour que l'automation du receveur ne l'écrase pas.
    /// </remarks>
    public const uint LockKey = 0x4C4B5031;   // « LKP1 »

    private readonly ApiVersion _version;
    private readonly GetStateBase64 _getState;
    private readonly ApplyState _applyState;
    private readonly RevertState _revertState;
    private readonly UnlockState _unlockState;
    private readonly UnlockAll _unlockAll;

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        _version     = new ApiVersion(pi);
        _getState    = new GetStateBase64(pi);
        _applyState  = new ApplyState(pi);
        _revertState = new RevertState(pi);
        _unlockState = new UnlockState(pi);
        _unlockAll   = new UnlockAll(pi);
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

    /// <summary>
    /// Relâche tous les verrous posés avec notre clé, sans rien changer d'autre.
    /// </summary>
    /// <remarks>
    /// Non destructif, contrairement à <see cref="Release"/> : cela ne touche
    /// pas à l'apparence, cela retire seulement une éventuelle mainmise de notre
    /// part. Sert quand on soupçonne qu'un verrou oublié gêne un autre plugin.
    /// </remarks>
    public int UnlockEverything()
    {
        try
        {
            return _unlockAll.Invoke(LockKey);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Rend le personnage à son état normal et relâche notre verrou.</summary>
    public void Release(int objectIndex)
    {
        _revertState.Invoke(objectIndex, LockKey, ApplyFlag.Equipment | ApplyFlag.Customization);
        _unlockState.Invoke(objectIndex, LockKey);
    }
}
