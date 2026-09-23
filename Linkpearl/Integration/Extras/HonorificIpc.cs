using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Honorific 3.x, relevé dans Honorific 9d66b63 (IpcProvider.cs).
/// </summary>
/// <remarks>
/// Honorific efface de lui-même, toutes les cinq secondes, le titre d'un
/// personnage qui n'est plus visible : le moteur repose tout à la réapparition.
/// </remarks>
public sealed class HonorificIpc : IDisposable
{
    private readonly ICallGateSubscriber<(uint, uint)> _version;
    private readonly ICallGateSubscriber<string> _getLocal;
    private readonly ICallGateSubscriber<int, string, object> _set;
    private readonly ICallGateSubscriber<int, object> _clear;
    private readonly ICallGateSubscriber<string, object> _localChanged;
    private readonly ICallGateSubscriber<object> _ready;

    public HonorificIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        _getLocal = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
        _set = pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _clear = pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
        _localChanged = pi.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
        _ready = pi.GetIpcSubscriber<object>("Honorific.Ready");
        _localChanged.Subscribe(OnLocalChanged);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 3;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal()
    {
        var json = _getLocal.InvokeFunc();
        return string.IsNullOrEmpty(json) ? null : json;
    }

    public void Apply(IGameObject target, string data) => _set.InvokeAction(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _clear.InvokeAction(target.ObjectIndex);

    private void OnLocalChanged(string _) => Changed?.Invoke();

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _localChanged.Unsubscribe(OnLocalChanged);
        _ready.Unsubscribe(OnReady);
    }
}
