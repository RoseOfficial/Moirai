using System.Numerics;

namespace Moirai.Core.Model;

public sealed record PlayerSnapshot(
    Vector3 Position,
    int Level,
    bool IsMelee,
    bool IsDead,
    bool InCombat,
    bool IsMounted,
    bool IsFlying,
    bool IsCasting,
    bool IsBetweenAreas,
    bool IsJumping,
    bool IsBeingMoved,
    bool IsOccupied,
    bool IsLevelSynced,
    bool CanMount,
    bool CanFly,
    ulong? TargetId,
    uint? ActiveMinionId,
    bool YokaiWatchActive,
    bool YokaiWatchOwned);
