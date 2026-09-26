using Dalamud.Plugin.Ipc;
using Moirai.Core.Intents;

namespace Moirai.Ipc;

// BossMod Reborn as the dodge layer (§6, H1–H3). Its AI is on with the combat backend, with its
// actions forbidden (the rotation is RotationSolver's), its follow modes off, and its movement
// forbidden by default so vnavmesh keeps moving the character; movement is handed over while
// danger is up. The hint gates are Reborn's own; vanilla BossMod is not driven.
public sealed class DodgeIpc
{
    private const float ImminentSeconds = 3f; // H2: a marked zone going off within this is danger

    private readonly LoadedPluginCheck _loaded = new("BossModReborn");
    private readonly ICallGateSubscriber<bool> _navigating;
    private readonly ICallGateSubscriber<int> _zones;
    private readonly ICallGateSubscriber<float> _nextActivation;
    private readonly ICallGateSubscriber<List<string>, bool, List<string>> _configure;
    private readonly ICallGateSubscriber<bool, bool> _pauseMovement;
    private bool? _ai;
    private MovementOwner? _owner;

    public DodgeIpc()
    {
        var pi = Svc.PluginInterface;
        _navigating = pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        _zones = pi.GetIpcSubscriber<int>("BossMod.Hints.ForbiddenZonesCount");
        _nextActivation = pi.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
        _configure = pi.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration");
        _pauseMovement = pi.GetIpcSubscriber<bool, bool>("BossMod.AI.PauseMovement");
    }

    public bool Installed => _loaded.Loaded;

    // H2: Reborn is steering us, or a marked zone is about to go off
    public bool Danger()
    {
        try
        {
            if (_navigating.InvokeFunc()) return true;
            return _zones.InvokeFunc() > 0 && _nextActivation.InvokeFunc() < ImminentSeconds;
        }
        catch
        {
            return false; // not loaded, or a build without the hint gates
        }
    }

    // H1: toggled with the combat backend, de-duplicated. The AI's on and off commands print nothing;
    // its settings go through Reborn's configuration gate, since each settings command echoes a line
    // to chat, and the AI is switched on once a fate or more
    public void SetAi(bool on)
    {
        if (!Installed || _ai == on) return;
        _ai = on;
        if (on)
        {
            Send("on");
            Configure("ForbidActions", true, "forbidactions on");
            Configure("FollowTarget", false, "followtarget off");
            Configure("FollowDuringCombat", false, "followcombat off");
            Configure("FollowDuringActiveBossModule", false, null); // the chat command above sets it too
            Configure("FollowOutOfCombat", false, "followoutofcombat off");
            _owner = null;
            SetMovement(MovementOwner.Navigation);
        }
        else
        {
            Send("off");
            _owner = null;
        }
    }

    // H2/H3: who moves the character while the AI is on, through Reborn's own gate (no chat line);
    // a build without it gets the chat command
    public void SetMovement(MovementOwner owner)
    {
        if (!Installed || _owner == owner) return;
        _owner = owner;
        var forbid = owner == MovementOwner.Navigation;
        try { _pauseMovement.InvokeFunc(forbid); }
        catch { Send(forbid ? "forbidmovement on" : "forbidmovement off"); }
    }

    // One AI setting through Reborn's configuration gate: changed in memory, not saved to the user's
    // own config, and not echoed to chat. The gate answers nothing on success; a build without it, or
    // one that does not know the field, gets the chat command instead.
    private void Configure(string field, bool value, string? fallback)
    {
        try
        {
            if (_configure.InvokeFunc(["AIConfig", field, value ? "true" : "false"], false).Count == 0) return;
        }
        catch
        {
            // a build without the gate
        }
        if (fallback is not null) Send(fallback);
    }

    public void Reset()
    {
        _ai = null;
        _owner = null;
    }

    private static void Send(string args) => Svc.Commands.ProcessCommand($"/bmrai {args}");
}
