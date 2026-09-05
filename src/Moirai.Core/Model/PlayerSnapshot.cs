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
    bool CompanionSummoned = false,
    int CompanionTimeLeftSeconds = 0,
    uint CompanionStanceId = 0, // BuddyAction row id of the active stance
    uint? ActiveMinionId = null, // §8: the summoned minion's Companion row id
    bool WatchEquipped = false,  // E1: the Yo-kai Watch is in the wrist slot
    bool WatchOwned = false);    // E1: in the bags or worn
