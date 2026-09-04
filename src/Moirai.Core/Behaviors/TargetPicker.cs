using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public static class TargetPicker
{
    // B13: an enemy with this many times the max HP of the smallest in the pool is the fight's objective
    public const float BossHpRatio = 2f;

    // Sticky first; then B7 peel; then B13 the boss among its adds; then whatever attacks us; then nearest.
    public static EnemySnapshot? Choose(
        IReadOnlyList<EnemySnapshot> enemies, uint fateId, ulong? sticky, Vector3 playerPos)
    {
        var pool = enemies.Where(e => e.IsAlive && e.FateId == fateId).ToList();
        if (pool.Count == 0) return null;
        if (sticky is { } s && pool.FirstOrDefault(e => e.Id == s) is { } kept) return kept;
        return pool.FirstOrDefault(e => e.TargetsProtectedFriendly)
            ?? Boss(pool)
            ?? pool.FirstOrDefault(e => e.IsAttackingPlayer)
            ?? pool.MinBy(e => Vector3.Distance(playerPos, e.Position));
    }

    // B13: a boss with adds. The fate sheet does not name the objective, and a boss's adds can
    // respawn for as long as it lives, so the enemy whose max HP dwarfs the smallest in the pool
    // is the one to fight. A pool that only differs by a level's worth of HP has no boss.
    private static EnemySnapshot? Boss(List<EnemySnapshot> pool)
    {
        var smallest = pool.Min(e => e.MaxHp);
        if (smallest == 0) return null; // HP unknown: nothing to compare
        var biggest = pool.MaxBy(e => e.MaxHp)!;
        return biggest.MaxHp >= smallest * BossHpRatio ? biggest : null;
    }
}
