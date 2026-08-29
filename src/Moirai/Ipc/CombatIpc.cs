using Moirai.Core.Intents;

namespace Moirai.Ipc;

// Combat backend v1: RotationSolver Reborn driven over its command surface.
// Olympus IPC and Wrath's lease model plug in behind the same three calls later.
public sealed class CombatIpc
{
    private bool? _lastEnabled;

    public bool RotationSolverInstalled
        => Svc.PluginInterface.InstalledPlugins.Any(p =>
            p.IsLoaded && p.InternalName.Contains("RotationSolver", StringComparison.OrdinalIgnoreCase));

    public void Set(bool enabled, CombatMode mode)
    {
        if (_lastEnabled == enabled) return;
        _lastEnabled = enabled;
        var command = enabled
            ? mode == CombatMode.Defensive ? "/rotation manual" : "/rotation auto"
            : "/rotation off";
        Svc.Commands.ProcessCommand(command);
    }

    public void ResetCache() => _lastEnabled = null;
}
