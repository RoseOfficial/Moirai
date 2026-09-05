using Moirai.Core.Model;

namespace Moirai.Core.Planning;

// D5/D8: the run pauses with a plain reason while a required plugin is missing. vnavmesh not
// ready is held for as long as it takes, since a mesh may still be building; a combat backend
// or TextAdvance that is not loaded gets a grace period and then the run stops as lost.
public sealed class DependencyWatch(long graceMs = 60_000)
{
    private long? _missingSinceMs;

    // null while everything is present; otherwise the status to show and whether the grace is over
    public (string Note, bool Lost)? Tick(WorldSnapshot w)
    {
        if (!w.NavmeshReady)
        {
            _missingSinceMs = null;
            return ("paused: vnavmesh is not ready", false);
        }
        var missing = !w.CombatReady ? "RotationSolver Reborn is not loaded"
            : !w.TextAdvanceReady ? "TextAdvance is not loaded"
            : null;
        if (missing is null)
        {
            _missingSinceMs = null;
            return null;
        }
        _missingSinceMs ??= w.NowMs;
        return ($"paused: {missing}", w.NowMs - _missingSinceMs >= graceMs);
    }
}
