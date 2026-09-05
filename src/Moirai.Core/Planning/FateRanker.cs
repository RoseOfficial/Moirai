using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public static class FateRanker
{
    public static FateSnapshot? PickBest(WorldSnapshot w, SelectionConfig c, long? lastFateEndEpoch = null, SessionSkipList? skips = null)
    {
        var eligible = w.Fates.Where(f => SelectionGates.Evaluate(f, w, c, skips) == SkipReason.None).ToList();
        if (eligible.Count == 0) return null;

        // A9: inside a ring or within the nearby radius wins outright, nearest first
        if (Nearest(eligible.Where(f => IsNearby(f, w, c)), w) is { } nearby)
            return nearby;

        // A10: shortly after a completion, only nearby (chain) spawns are considered
        if (lastFateEndEpoch is { } end && w.NowEpoch - end < c.PostFateGraceSeconds)
            return null;

        return eligible.Min(Comparer<FateSnapshot>.Create((x, y) => RankCompare(x, y, w, c)));
    }

    // A9/A13: the nearest eligible fate inside the nearby radius, or null; asked again on the way
    public static FateSnapshot? PickNearby(WorldSnapshot w, SelectionConfig c, SessionSkipList? skips = null)
        => Nearest(w.Fates.Where(f => IsNearby(f, w, c) && SelectionGates.Evaluate(f, w, c, skips) == SkipReason.None), w);

    public static bool IsNearby(FateSnapshot f, WorldSnapshot w, SelectionConfig c)
    {
        var d = Vector3.Distance(w.Player.Position, f.Position);
        return d <= c.NearbyOverrideDistance || f.Contains(w.Player.Position);
    }

    private static FateSnapshot? Nearest(IEnumerable<FateSnapshot> fates, WorldSnapshot w)
        => fates.MinBy(f => Vector3.Distance(w.Player.Position, f.Position));

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
