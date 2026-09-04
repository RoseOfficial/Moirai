using System.Numerics;

namespace Moirai.Core.Model;

public sealed record EnemySnapshot(
    ulong Id,
    Vector3 Position,
    float HitboxRadius,
    uint FateId,
    bool IsAlive,
    bool TargetsProtectedFriendly,
    bool IsAttackingPlayer,
    uint MaxHp);
