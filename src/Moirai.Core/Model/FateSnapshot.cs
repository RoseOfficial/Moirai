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
    uint EventItemId,
    FateKind SheetKind = FateKind.Battle) // B12: the kind by the sheet alone, whatever the opened state says
{
    public const long UnopenedDefaultSeconds = 900;

    // A8/B11: nobody has opened this fate yet; FateClassifier.IsUnopened holds the signal
    public bool IsUnopened => FateClassifier.IsUnopened(Phase, StartTimeEpoch, Progress);

    // A8: a FATE that hasn't been opened by its starter NPC reports no timer
    public long EffectiveTimeLeft => IsUnopened ? UnopenedDefaultSeconds : TimeRemainingSeconds;

    // The ring is a horizontal circle: the floor inside it can sit well above or below the center
    public bool Contains(Vector3 pos) => Geometry.HorizontalDistance(pos, Position) <= Radius;
}
