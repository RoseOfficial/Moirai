using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public enum InterruptKind { None, Busy, Dead, UnexpectedCombat, NavmeshNotReady }

public static class InterruptEvaluator
{
    // Spec §3 order: busy guard, death, unexpected combat, navmesh.
    public static InterruptKind Evaluate(WorldSnapshot w, uint? currentFateId, bool inFatePhase = false)
    {
        var p = w.Player;
        if (p.IsCasting || p.IsBetweenAreas || p.IsJumping || p.IsBeingMoved || p.IsOccupied || w.LifestreamBusy)
            return InterruptKind.Busy;
        if (p.IsDead)
            return InterruptKind.Dead;
        if (p.InCombat && !IsFateCombat(w, currentFateId, inFatePhase))
            return InterruptKind.UnexpectedCombat;
        if (!w.NavmeshReady)
            return InterruptKind.NavmeshNotReady;
        return InterruptKind.None;
    }

    // D3: combat inside the ring of a running fate is that fate's own. So is combat anywhere once
    // we are fighting it: mobs get pulled past the edge and knockbacks land outside, and pausing
    // there would leave nobody to walk back in (the engage behavior does, C5).
    private static bool IsFateCombat(WorldSnapshot w, uint? currentFateId, bool inFatePhase)
    {
        if (currentFateId is not { } id) return false;
        var f = w.FateById(id);
        if (f is not { Phase: FatePhase.Running }) return false;
        return inFatePhase || f.Contains(w.Player.Position);
    }
}
