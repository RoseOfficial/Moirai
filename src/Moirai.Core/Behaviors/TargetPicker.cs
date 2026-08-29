using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public static class TargetPicker
{
    // Sticky first; then B7 peel; then whatever attacks us; then nearest.
    public static EnemySnapshot? Choose(
        IReadOnlyList<EnemySnapshot> enemies, uint fateId, ulong? sticky, Vector3 playerPos)
    {
        var pool = enemies.Where(e => e.IsAlive && e.FateId == fateId).ToList();
        if (pool.Count == 0) return null;
        if (sticky is { } s && pool.FirstOrDefault(e => e.Id == s) is { } kept) return kept;
        return pool.FirstOrDefault(e => e.TargetsProtectedFriendly)
            ?? pool.FirstOrDefault(e => e.IsAttackingPlayer)
            ?? pool.MinBy(e => Vector3.Distance(playerPos, e.Position));
    }
}
