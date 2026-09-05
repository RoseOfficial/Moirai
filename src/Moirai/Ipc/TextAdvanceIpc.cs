using Dalamud.Plugin.Ipc;
using Moirai.Game;

namespace Moirai.Ipc;

// TextAdvance drives every Talk window and the collect hand-in window (item slot, hand over)
// while Moirai holds its external control for the run. It never confirms the fate-start
// yes/no; that one is Moirai's (B4). Control is re-asserted while running because TextAdvance
// drops it on its own after a zone change.
public sealed class TextAdvanceIpc
{
    private const string Owner = "Moirai";

    // Field names mirror TextAdvance's own config type: Dalamud IPC carries the object as JSON,
    // so only the names have to agree. Quest accept/complete and auto-interact stay off:
    // Moirai targets and interacts itself and never touches quests.
    public sealed class ExternalTerritoryConfig
    {
        public bool? EnableQuestAccept;
        public bool? EnableQuestComplete;
        public bool? EnableRewardPick;
        public bool? EnableRequestHandin;
        public bool? EnableCutsceneEsc;
        public bool? EnableCutsceneSkipConfirm;
        public bool? EnableTalkSkip;
        public bool? EnableRequestFill;
        public bool? EnableAutoInteract;
    }

    private static readonly ExternalTerritoryConfig RunConfig = new()
    {
        EnableTalkSkip = true,
        EnableRequestFill = true,
        EnableRequestHandin = true,
        EnableRewardPick = true,
        EnableCutsceneEsc = true,
        EnableCutsceneSkipConfirm = true,
    };

    private readonly LoadedPluginCheck _loaded = new("TextAdvance");
    private readonly ICallGateSubscriber<bool> _inControl;
    private readonly ICallGateSubscriber<string, ExternalTerritoryConfig, bool> _enable;
    private readonly ICallGateSubscriber<string, bool> _disable;

    public TextAdvanceIpc()
    {
        var pi = Svc.PluginInterface;
        _inControl = pi.GetIpcSubscriber<bool>("TextAdvance.IsInExternalControl");
        _enable = pi.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>("TextAdvance.EnableExternalControl");
        _disable = pi.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl");
    }

    public bool Installed => _loaded.Loaded;

    public bool InControl()
    {
        try { return _inControl.InvokeFunc(); }
        catch { return false; }
    }

    public void Take()
    {
        if (!Installed) return;
        try { _enable.InvokeFunc(Owner, RunConfig); }
        catch { /* gate missing or TextAdvance mid-reload: the next re-assert tries again */ }
    }

    // Called every tick while running; cheap, and the only way to notice a dropped control
    public void Reassert()
    {
        if (Throttle.Try("moirai.textadvance", 2000) && !InControl())
            Take();
    }

    public void Release()
    {
        try { _disable.InvokeFunc(Owner); }
        catch { /* not installed */ }
    }
}
