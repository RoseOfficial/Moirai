using System.Numerics;

namespace Moirai.Core.Model;

public sealed record FateSnapshot(
    uint Id,
    Vector3 Position,
    float Radius,
    int Progress,
    FatePhase Phase,
    FateKind Kind,
    int MaxLevel,
    bool IsBonus,
    bool IsSpecialBoss,
    bool HasContinuation,
    long StartTimeEpoch,
    long TimeRemainingSeconds,
    uint EventItemId)
{
    public const long UnopenedDefaultSeconds = 900;

    // A8: a FATE that hasn't been opened by its starter NPC reports no timer
    public long EffectiveTimeLeft => StartTimeEpoch == 0 ? UnopenedDefaultSeconds : TimeRemainingSeconds;

    // The ring is a horizontal circle: the floor inside it can sit well above or below the center
    public bool Contains(Vector3 pos) => Geometry.HorizontalDistance(pos, Position) <= Radius;
}
