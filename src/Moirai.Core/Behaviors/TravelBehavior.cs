using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class TravelBehavior(MovementConfig cfg) : IBehavior
{
    public const int MaxRerolls = 8;

    private int _rerolls;

    public Vector3? CurrentDropoff { get; private set; }
    public bool RerollsExhausted => _rerolls >= MaxRerolls;

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("TravelBehavior requires a fate");
        if (fate.Phase is FatePhase.Ended or FatePhase.Failed)
            return new(new NoAction(), BehaviorStatus.Failed, "fate gone");

        CurrentDropoff ??= PickDropoff(fate, ctx);
        if (CurrentDropoff is not { } dropoff)
            return new(new NoAction(), BehaviorStatus.Failed, "no landable point");

        var p = w.Player;
        var distToDrop = Vector3.Distance(p.Position, dropoff);
        var insideRing = Vector3.Distance(p.Position, fate.Position) <= fate.Radius;

        if (distToDrop <= cfg.ArriveTolerance && insideRing)
        {
            return p.IsMounted
                ? new(new Dismount(), BehaviorStatus.Running, "dismounting")
                : new(new NoAction(), BehaviorStatus.Done, "arrived");
        }

        // C12: mount only when the leg is worth it and mounting is legal
        if (!p.IsMounted && p.CanMount && !p.InCombat && distToDrop > cfg.MountLegThreshold)
            return new(new MountUp(), BehaviorStatus.Running, "mounting");

        // C1: zone override beats flight unlock
        var fly = p.IsMounted && p.CanFly && ctx.ZoneFlightAllowed;
        return new(new GoTo(dropoff, fly, cfg.ArriveTolerance), BehaviorStatus.Running, "moving");
    }

    public void RerollDropoff()
    {
        CurrentDropoff = null;
        _rerolls++;
    }

    public void Reset()
    {
        CurrentDropoff = null;
        _rerolls = 0;
    }

    // C3: randomized in-ring point resolved to the mesh floor; never the raw center
    private static Vector3? PickDropoff(FateSnapshot fate, BehaviorContext ctx)
    {
        for (var i = 0; i < MaxRerolls; i++)
        {
            var angle = ctx.Random.NextDouble() * Math.PI * 2;
            var r = fate.Radius * 0.75f * (float)Math.Sqrt(ctx.Random.NextDouble());
            var candidate = fate.Position + new Vector3(
                (float)Math.Cos(angle) * r, 0, (float)Math.Sin(angle) * r);
            if (ctx.Landing.ResolveFloor(candidate) is { } grounded)
                return grounded;
        }
        return null;
    }
}
