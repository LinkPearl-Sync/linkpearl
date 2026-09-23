using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// Moodles version 4, relevé dans Moodles f4b7578 (IPCProcessor.cs).
/// </summary>
/// <remarks>
/// Par adresse et jamais par nom : les variantes « ByName » prennent
/// « Nom@Monde ». Moodles ignore en silence un personnage qu'il n'a pas encore
/// vu s'afficher, d'où l'attente de chargement avant de poser.
/// </remarks>
public sealed class MoodlesIpc : IDisposable
{
    private readonly ICallGateSubscriber<int> _version;
    private readonly ICallGateSubscriber<nint, string> _get;
    private readonly ICallGateSubscriber<nint, string, object> _set;
    private readonly ICallGateSubscriber<nint, object> _clear;
    private readonly ICallGateSubscriber<nint, object> _modified;
    private readonly ICallGateSubscriber<object> _ready;
    private readonly Func<nint> _localAddress;

    public MoodlesIpc(IDalamudPluginInterface pi, Func<nint> localAddress)
    {
        _localAddress = localAddress;
        _version = pi.GetIpcSubscriber<int>("Moodles.Version");
        _get = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        _set = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        _clear = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");
        _modified = pi.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        _ready = pi.GetIpcSubscriber<object>("Moodles.Ready");
        _modified.Subscribe(OnModified);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _version.InvokeFunc() == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal(nint localAddress)
    {
        var data = _get.InvokeFunc(localAddress);
        return string.IsNullOrEmpty(data) ? null : data;
    }

    public void Apply(IGameObject target, string data) => _set.InvokeAction(target.Address, data);

    public void Clear(IGameObject target) => _clear.InvokeAction(target.Address);

    /// <summary>Levé pour n'importe quel personnage suivi : on ne garde que le nôtre.</summary>
    private void OnModified(nint address)
    {
        if (address != nint.Zero && address == _localAddress())
            Changed?.Invoke();
    }

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _modified.Unsubscribe(OnModified);
        _ready.Unsubscribe(OnReady);
    }
}
