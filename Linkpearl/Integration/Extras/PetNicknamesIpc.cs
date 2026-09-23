using System.Globalization;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Linkpearl.Core.Manifest;

namespace Linkpearl.Integration.Extras;

/// <summary>
/// PetNicknames (InternalName PetRenamer) 4.x, relevé dans FFXIVPetRenamer
/// 7192264 (IPC/IpcProvider.cs).
/// </summary>
/// <remarks>
/// Les données reçues sont neutralisées : ni nom, ni monde, ni ContentId. On y
/// remet ici ceux du personnage visé, lus chez nous, juste avant de poser, et
/// ils ne sortent pas de cette classe : ni journal, ni noyau. <c>SetPlayerDataV2</c>
/// vérifie que le ContentId passé est bien celui des données.
///
/// <c>ClearPlayerData</c> V1 n'est jamais employé : il retrouve l'entrée par nom
/// et effacerait un surnom que l'utilisateur aurait importé lui-même.
/// </remarks>
public sealed class PetNicknamesIpc : IDisposable
{
    private readonly ICallGateSubscriber<bool> _enabled;
    private readonly ICallGateSubscriber<(uint, uint)> _version;
    private readonly ICallGateSubscriber<string> _get;
    private readonly ICallGateSubscriber<ulong, string, object> _set;
    private readonly ICallGateSubscriber<nint, object> _clear;
    private readonly ICallGateSubscriber<string, object> _changed;
    private readonly ICallGateSubscriber<object> _ready;

    public PetNicknamesIpc(IDalamudPluginInterface pi)
    {
        _enabled = pi.GetIpcSubscriber<bool>("PetRenamer.IsEnabled");
        _version = pi.GetIpcSubscriber<(uint, uint)>("PetRenamer.ApiVersion");
        _get = pi.GetIpcSubscriber<string>("PetRenamer.GetPlayerData");
        _set = pi.GetIpcSubscriber<ulong, string, object>("PetRenamer.SetPlayerDataV2");
        _clear = pi.GetIpcSubscriber<nint, object>("PetRenamer.ClearPlayerDataV2");
        _changed = pi.GetIpcSubscriber<string, object>("PetRenamer.OnPlayerDataChanged");
        _ready = pi.GetIpcSubscriber<object>("PetRenamer.OnReady");
        _changed.Subscribe(OnChanged);
        _ready.Subscribe(OnReady);
    }

    public event Action? Changed;

    public event Action? Ready;

    public bool IsAvailable()
    {
        try
        {
            return _enabled.InvokeFunc() && _version.InvokeFunc().Item1 == 4;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? ReadLocal()
    {
        var data = _get.InvokeFunc();
        return string.IsNullOrEmpty(data) ? null : data;
    }

    public unsafe void Apply(IGameObject target, string neutral)
    {
        if (target is not IPlayerCharacter player
            || PetNicknamesData.TrySplit(neutral, out var lines, out var separator) is false)
            return;

        var contentId = ((Character*)player.Address)->ContentId;

        lines[1] = player.Name.TextValue;
        lines[2] = player.HomeWorld.RowId.ToString(CultureInfo.InvariantCulture);
        lines[3] = contentId.ToString(CultureInfo.InvariantCulture);

        _set.InvokeAction(contentId, PetNicknamesData.Join(lines, separator));
    }

    public void Clear(IGameObject target) => _clear.InvokeAction(target.Address);

    private void OnChanged(string _) => Changed?.Invoke();

    private void OnReady() => Ready?.Invoke();

    public void Dispose()
    {
        _changed.Unsubscribe(OnChanged);
        _ready.Unsubscribe(OnReady);
    }
}
