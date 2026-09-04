using Dalamud.Plugin.Ipc;
using Moirai.Core.Intents;
using Moirai.Core.Planning;

namespace Moirai.Ipc;

// Combat backend v1: RotationSolver Reborn driven over its command surface.
// Olympus IPC and Wrath's lease model plug in behind the same three calls later.
public sealed class CombatIpc
{
    private readonly ICallGateSubscriber<bool> _active;
    private RotationMode? _last;
    private bool _fateFilterSuspended;

    public CombatIpc()
    {
        _active = Svc.PluginInterface.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive");
    }

    public bool RotationSolverInstalled
        => Svc.PluginInterface.InstalledPlugins.Any(p =>
            p.IsLoaded && p.InternalName.Contains("RotationSolver", StringComparison.OrdinalIgnoreCase));

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
            Svc.Commands.ProcessCommand(step switch
            {
                RotationMode.Auto => "/rotation auto",
                RotationMode.Manual => "/rotation manual",
                _ => "/rotation off",
            });
        _last = want;
        if (!defensive) SuspendFateFilter(false);
    }

    public void ResetCache() => _last = null;

    // RSR's "Ignore Non-Fate targets while in a Fate" option, set in memory only (RSR does not save
    // it from this command) and put back to its default afterwards.
    private void SuspendFateFilter(bool suspend)
    {
        if (suspend == _fateFilterSuspended) return;
        _fateFilterSuspended = suspend;
        Svc.Commands.ProcessCommand($"/rotation settings IgnoreNonFateInFate {(suspend ? "false" : "true")}");
    }
}
