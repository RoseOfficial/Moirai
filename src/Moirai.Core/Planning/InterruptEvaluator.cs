using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public enum InterruptKind { None, Busy, Dead, UnexpectedCombat, NavmeshNotReady }

public static class InterruptEvaluator
{
    // Spec §3 order: busy guard, death, unexpected combat, navmesh.
    public static InterruptKind Evaluate(WorldSnapshot w, uint? currentFateId)
    {
        var p = w.Player;
        if (p.IsCasting || p.IsBetweenAreas || p.IsJumping || p.IsBeingMoved || p.IsOccupied || w.LifestreamBusy)
            return InterruptKind.Busy;
        if (p.IsDead)
            return InterruptKind.Dead;
        if (p.InCombat && !InsideCurrentFate(w, currentFateId))
            return InterruptKind.UnexpectedCombat;
        if (!w.NavmeshReady)
            return InterruptKind.NavmeshNotReady;
        return InterruptKind.None;
    }

    private static bool InsideCurrentFate(WorldSnapshot w, uint? currentFateId)
    {
        if (currentFateId is not { } id) return false;
        var f = w.FateById(id);
        return f is { Phase: FatePhase.Running }
            && f.Contains(w.Player.Position);
    }
}
