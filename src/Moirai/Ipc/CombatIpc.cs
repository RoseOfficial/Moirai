using Dalamud.Plugin.Ipc;
using Moirai.Core.Intents;
using Moirai.Core.Planning;

namespace Moirai.Ipc;

// Combat backend v1: RotationSolver Reborn driven over its command surface.
// Olympus IPC and Wrath's lease model plug in behind the same three calls later.
public sealed class CombatIpc
{
    // RSR's StateCommandType and TargetingType, mirrored by value: Dalamud's IPC converts a foreign
    // enum through JSON, so only the numbers cross the boundary and they must follow RSR's order.
    private enum RsrState : byte { Off, Auto, TargetOnly, Manual, AutoDuty }
    private enum RsrTargeting : byte { Big, Small, HighHP, LowHP, HighHPPercent, LowHPPercent, HighMaxHP }

    private readonly LoadedPluginCheck _loaded = new("RotationSolver");
    private readonly ICallGateSubscriber<bool> _active;
    private readonly ICallGateSubscriber<RsrState, RsrTargeting, object> _autoWithTargeting;
    private RotationMode? _last;
    private bool _fateFilterSuspended;

    public CombatIpc()
    {
        var pi = Svc.PluginInterface;
        _active = pi.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive");
        _autoWithTargeting = pi.GetIpcSubscriber<RsrState, RsrTargeting, object>("RotationSolverReborn.AutodutyChangeOperatingMode");
    }

    public bool RotationSolverInstalled => _loaded.Loaded;

    // Whether RSR reports itself on; null when it cannot say (not loaded, or too old to report)
    public bool? IsActive()
    {
        try { return _active.InvokeFunc(); }
        catch { return null; }
    }

    // Auto lets RSR pick its own targets among the fate's enemies. Defensive is a stray clear: RSR's
    // manual mode attacks the held target alone, and the planner holds the stray. RSR refuses any
    // mob that is not the fate's while the game counts us inside a fate, whatever the mode, so its
    // fate filter is set aside for the clear and restored the moment anything else is asked for.
    public void Set(bool enabled, CombatMode mode)
    {
        var want = !enabled ? RotationMode.Off
            : mode == CombatMode.Defensive ? RotationMode.Manual
            : RotationMode.Auto;
        var defensive = want == RotationMode.Manual;
        if (defensive) SuspendFateFilter(true);
        foreach (var step in RotationSwitch.Plan(_last, IsActive(), want))
            Send(step);
        _last = want;
        if (!defensive) SuspendFateFilter(false);
    }

    public void ResetCache() => _last = null;

    // B13: auto enters through RSR's AutoDuty entry point, which carries a targeting order with it.
    // RSR's own default sorts the fate's enemies by lowest HP, which in a boss fight is always an
    // add; sorted by highest max HP the boss wins whenever it can be targeted, so the backend and
    // the planner agree on it. The override is transient (off clears it), and the same entry point
    // spares us RSR's out-of-combat auto-off. A build without the gate gets the chat command and
    // RSR's own targeting order.
    private void Send(RotationMode step)
    {
        if (step == RotationMode.Auto)
        {
            try
            {
                _autoWithTargeting.InvokeAction(RsrState.AutoDuty, RsrTargeting.HighMaxHP);
                return;
            }
            catch { /* gate missing on this build */ }
        }
        Svc.Commands.ProcessCommand(step switch
        {
            RotationMode.Auto => "/rotation auto",
            RotationMode.Manual => "/rotation manual",
            _ => "/rotation off",
        });
    }

    // RSR's "Ignore Non-Fate targets while in a Fate" option, set in memory only (RSR does not save
    // it from this command) and put back to its default afterwards.
    private void SuspendFateFilter(bool suspend)
    {
        if (suspend == _fateFilterSuspended) return;
        _fateFilterSuspended = suspend;
        Svc.Commands.ProcessCommand($"/rotation settings IgnoreNonFateInFate {(suspend ? "false" : "true")}");
    }
}
