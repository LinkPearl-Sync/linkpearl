using Dalamud.Plugin;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;

namespace Linkpearl.Integration;

/// <summary>
/// Adaptateur mince vers Glamourer.
/// </summary>
public sealed class GlamourerIpc : IDisposable
{
    /// <summary>
    /// Notre clé, constante et non nulle.
    /// </summary>
    /// <remarks>
    /// <b>Passer une clé non nulle à ApplyState verrouille l'état</b>, même sans
    /// le drapeau <c>Lock</c>. Constaté en jeu : un « unlock » a relâché deux
    /// verrous alors que le code affirmait en commentaire n'en poser aucun. La
    /// documentation de l'API dit « a key to unlock or lock the state if
    /// necessary », ce que j'avais lu comme « pour déverrouiller » seulement.
    ///
    /// Conséquence : on applique sans clé quand on s'applique à soi-même, sinon
    /// l'utilisateur ne peut plus reprendre la main depuis l'interface de
    /// Glamourer. La clé ne servira que pour l'apparence d'un pair, où le
    /// verrou est souhaitable pour que l'automation du receveur ne l'écrase pas.
    /// </remarks>
    public const uint LockKey = 0x4C4B5031;   // « LKP1 »

    private readonly ApiVersion _version;
    private readonly GetStateBase64 _getState;
    private readonly ApplyState _applyState;
    private readonly RevertState _revertState;
    private readonly UnlockState _unlockState;
    private readonly UnlockAll _unlockAll;
    private readonly IDisposable _finalized;
    private readonly IDisposable _changed;

    public GlamourerIpc(IDalamudPluginInterface pi)
    {
        _version     = new ApiVersion(pi);
        _getState    = new GetStateBase64(pi);
        _applyState  = new ApplyState(pi);
        _revertState = new RevertState(pi);
        _unlockState = new UnlockState(pi);
        _unlockAll   = new UnlockAll(pi);

        // Les deux, et non StateFinalized seul : il ne se lève qu'à la fin d'un
        // changement groupé (design, retour aux valeurs du jeu). Une retouche
        // faite à la main dans Glamourer, une teinture ou une pièce, s'applique
        // en direct sans redessin et ne lève que StateChanged. Vu en jeu : sans
        // lui, rien ne suivait tant qu'on ne forçait pas un redessin. La rafale
        // qu'il produit est précisément ce qu'absorbe l'anti-rebond.
        _finalized = StateFinalized.Subscriber(pi, (address, _) => Changed?.Invoke(address));
        _changed   = StateChanged.Subscriber(pi, address => Changed?.Invoke(address));
    }

    /// <summary>L'état d'un objet a changé, avec son adresse.</summary>
    /// <remarks>Levé depuis le thread du jeu, parfois en rafale : ne rien faire de long ici.</remarks>
    public event Action<nint>? Changed;

    public void Dispose()
    {
        _finalized.Dispose();
        _changed.Dispose();
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

    /// <summary>Applique l'état d'un pair, verrouillé sous notre clé.</summary>
    /// <remarks>
    /// Verrouillé pour que l'automation de Glamourer chez le receveur n'écrase
    /// pas l'apparence du pair une seconde après l'avoir posée.
    /// </remarks>
    public void ApplyStateLocked(string base64, int objectIndex)
        => Apply(base64, objectIndex, LockKey);

    private void Apply(string base64, int objectIndex, uint key)
    {
        var ec = _applyState.Invoke(
            base64, objectIndex, key, ApplyFlag.Once | ApplyFlag.Equipment | ApplyFlag.Customization);

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
