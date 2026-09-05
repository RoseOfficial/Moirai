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
    private bool? _ai;
    private MovementOwner? _owner;

    public DodgeIpc()
    {
        var pi = Svc.PluginInterface;
        _navigating = pi.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        _zones = pi.GetIpcSubscriber<int>("BossMod.Hints.ForbiddenZonesCount");
        _nextActivation = pi.GetIpcSubscriber<float>("BossMod.Hints.ForbiddenZonesNextActivation");
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

    // H1: toggled with the combat backend; de-duplicated so the chat command is not spammed
    public void SetAi(bool on)
    {
        if (!Installed || _ai == on) return;
        _ai = on;
        if (on)
        {
            Send("on");
            Send("forbidactions on");
            Send("followtarget off");
            Send("followcombat off");
            Send("followoutofcombat off");
            _owner = null;
            SetMovement(MovementOwner.Navigation);
        }
        else
        {
            Send("off");
            _owner = null;
        }
    }

    // H2/H3: who moves the character while the AI is on
    public void SetMovement(MovementOwner owner)
    {
        if (!Installed || _owner == owner) return;
        _owner = owner;
        Send(owner == MovementOwner.Dodge ? "forbidmovement off" : "forbidmovement on");
    }

    public void Reset()
    {
        _ai = null;
        _owner = null;
    }

    private static void Send(string args) => Svc.Commands.ProcessCommand($"/bmrai {args}");
}
