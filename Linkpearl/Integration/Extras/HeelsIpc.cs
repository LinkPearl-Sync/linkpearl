using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// SimpleHeels 2.x, relevé dans SimpleHeels 162466c (ApiProvider.cs).
/// </summary>
/// <remarks>
/// SimpleHeels garde un décalage reçu tant qu'on ne le désenregistre pas, et ne
/// se désenregistre que sur un personnage visible : voir la limite connue de
/// la spec.
/// </remarks>
public sealed class HeelsIpc : IDisposable
{
    private readonly ICallGateSubscriber<(int, int)> _version;
    private readonly ICallGateSubscriber<string> _getLocal;
    private readonly ICallGateSubscriber<int, string, object?> _register;
    private readonly ICallGateSubscriber<int, object?> _unregister;
    private readonly ICallGateSubscriber<string, object?> _localChanged;

    public HeelsIpc(IDalamudPluginInterface pi)
    {
        _version = pi.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");
        _getLocal = pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");
        _register = pi.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        _unregister = pi.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");
        _localChanged = pi.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");
        _localChanged.Subscribe(OnLocalChanged);
    }

    public event Action? Changed;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc().Item1 == 2;
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

    public void Apply(IGameObject target, string data) => _register.InvokeAction(target.ObjectIndex, data);

    public void Clear(IGameObject target) => _unregister.InvokeAction(target.ObjectIndex);

    private void OnLocalChanged(string _) => Changed?.Invoke();

    public void Dispose() => _localChanged.Unsubscribe(OnLocalChanged);
}
