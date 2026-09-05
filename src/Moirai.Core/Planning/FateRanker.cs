using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public static class FateRanker
{
    public static FateSnapshot? PickBest(WorldSnapshot w, SelectionConfig c, long? lastFateEndEpoch = null, IReadOnlySet<uint>? deadly = null)
    {
        var eligible = w.Fates.Where(f => SelectionGates.Evaluate(f, w, c, deadly) == SkipReason.None).ToList();
        if (eligible.Count == 0) return null;

        // A9: inside a ring or within the nearby radius wins outright, nearest first
        var nearby = eligible.Where(f => IsNearby(f, w, c)).ToList();
        if (nearby.Count > 0)
            return nearby.MinBy(f => Vector3.Distance(w.Player.Position, f.Position));

        // A10: shortly after a completion, only nearby (chain) spawns are considered
        if (lastFateEndEpoch is { } end && w.NowEpoch - end < c.PostFateGraceSeconds)
            return null;

        return eligible.Min(Comparer<FateSnapshot>.Create((x, y) => RankCompare(x, y, w, c)));
    }

    private static bool IsNearby(FateSnapshot f, WorldSnapshot w, SelectionConfig c)
    {
        var d = Vector3.Distance(w.Player.Position, f.Position);
        return d <= c.NearbyOverrideDistance || f.Contains(w.Player.Position);
    }

    // negative when x outranks y; final tie-break lowest id (A11, determinism)
    private static int RankCompare(FateSnapshot x, FateSnapshot y, WorldSnapshot w, SelectionConfig c)
    {
        foreach (var crit in c.Priority)
        {
            var cmp = crit switch
            {
                SelectionCriterion.Progress => y.Progress.CompareTo(x.Progress),
                SelectionCriterion.Bonus => y.IsBonus.CompareTo(x.IsBonus),
                SelectionCriterion.TimeLeft => y.EffectiveTimeLeft.CompareTo(x.EffectiveTimeLeft),
                SelectionCriterion.Distance =>
                    Vector3.Distance(w.Player.Position, x.Position)
                        .CompareTo(Vector3.Distance(w.Player.Position, y.Position)),
                SelectionCriterion.DistanceTeleport =>
                    TravelCost(x, w, c).CompareTo(TravelCost(y, w, c)),
                _ => 0,
            };
            if (cmp != 0) return cmp;
        }
        return x.Id.CompareTo(y.Id);
    }

    // A12: min(direct, nearest-aetheryte-to-fate + penalty)
    private static float TravelCost(FateSnapshot f, WorldSnapshot w, SelectionConfig c)
    {
        var best = Vector3.Distance(w.Player.Position, f.Position);
        foreach (var a in w.Aetherytes)
        {
            var viaTeleport = Vector3.Distance(a.Position, f.Position) + c.TeleportPenalty;
            if (viaTeleport < best) best = viaTeleport;
        }
        return best;
    }
}
