using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Customize+ 6.x, relevé dans CustomizePlus 44d2541 (Api/CustomizePlusIpc.Profile.cs).
/// </summary>
/// <remarks>
/// Le profil posé sur un pair est temporaire : Customize+ l'oublie quand
/// l'armature disparaît, et le moteur repose tout à chaque réapparition.
/// </remarks>
public sealed class CustomizePlusIpc : IDisposable
{
    private readonly ICallGateSubscriber<(int, int)> _version;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _activeProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _profile;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTemporary;
    private readonly ICallGateSubscriber<ushort, int> _deleteTemporary;
    private readonly ICallGateSubscriber<ushort, Guid, object> _onUpdate;

    public CustomizePlusIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _activeProfile = pi.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _profile = pi.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _setTemporary = pi.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _deleteTemporary = pi.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _onUpdate = pi.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _onUpdate.Subscribe(OnUpdate);
    }

    public event Action? Changed;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 6;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Le profil actif du joueur local, ou null. Code 0 : succès.</summary>
    public string? ReadLocal()
    {
        var (found, id) = _activeProfile.InvokeFunc(0);

        if (found != 0 || id is not { } profileId || profileId == Guid.Empty)
            return null;

        var (read, json) = _profile.InvokeFunc(profileId);
        return read == 0 && string.IsNullOrEmpty(json) is false ? json : null;
    }

    public void Apply(IGameObject target, string data) => _setTemporary.InvokeFunc(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _deleteTemporary.InvokeFunc(target.ObjectIndex);

    private void OnUpdate(ushort objectIndex, Guid profile)
    {
        if (objectIndex == 0)
            Changed?.Invoke();
    }

    public void Dispose() => _onUpdate.Unsubscribe(OnUpdate);
}
