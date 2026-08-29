using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public interface IRandomSource
{
    double NextDouble();
}

public interface ILandingResolver
{
    Vector3? ResolveFloor(Vector3 near);
}

public sealed class MovementConfig
{
    public float MountLegThreshold { get; init; } = 30f;
    public float ArriveTolerance { get; init; } = 4f;
}

public sealed record BehaviorContext(
    FateSnapshot? Fate,
    bool ZoneFlightAllowed,
    IRandomSource Random,
    ILandingResolver Landing);

public enum BehaviorStatus { Running, Done, Failed }

public sealed record BehaviorStep(Intent Intent, BehaviorStatus Status, string Note);

public interface IBehavior
{
    BehaviorStep Tick(WorldSnapshot world, BehaviorContext ctx);
    void Reset();
}
