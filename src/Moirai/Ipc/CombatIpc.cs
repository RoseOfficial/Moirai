using Moirai.Core.Intents;

namespace Moirai.Ipc;

// Combat backend v1: RotationSolver Reborn driven over its command surface.
// Olympus IPC and Wrath's lease model plug in behind the same three calls later.
public sealed class CombatIpc
{
    private string? _lastCommand;

    public bool RotationSolverInstalled
        => Svc.PluginInterface.InstalledPlugins.Any(p =>
            p.IsLoaded && p.InternalName.Contains("RotationSolver", StringComparison.OrdinalIgnoreCase));

    // Both planner modes run RSR in auto. RSR's manual mode attacks only the held target, and the
    // planner holds none during a defensive clear, so manual left stray aggro unanswered. In auto
    // RSR picks its own targets; the engage behavior accepts any fate enemy it settles on.
    public void Set(bool enabled, CombatMode mode)
    {
        var command = enabled ? "/rotation auto" : "/rotation off";
        if (_lastCommand == command) return; // re-issuing a mode makes RSR flicker off and on
        _lastCommand = command;
        Svc.Commands.ProcessCommand(command);
    }

    public void ResetCache() => _lastCommand = null;
}
