using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Core.Behaviors;

public sealed class TravelBehavior(MovementConfig cfg) : IBehavior
{
    public const int MaxRerolls = 8;

    private readonly StuckDetector _stuck = new(cfg.StuckMinMove, cfg.StuckWindowMs);
    private int _rerolls;
    private bool _landing; // a dismount has been issued at this dropoff and has not taken yet

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
        var insideRing = fate.Contains(p.Position);
        var overDropoff = Geometry.HorizontalDistance(p.Position, dropoff) <= cfg.ArriveTolerance
                          && MathF.Abs(p.Position.Y - dropoff.Y) <= cfg.ArriveVerticalTolerance;
        var arrived = overDropoff && insideRing;

        // C12: mount only when the leg is worth it and mounting is legal
        var wantsMount = !p.IsMounted && p.CanMount && !p.InCombat && distToDrop > cfg.MountLegThreshold;

        // spec 7.2: no meaningful movement across the window while we expect to be moving;
        // the mount cast is a legitimate standstill
        var stalled = _stuck.Sample(p.Position, w.NowMs, suppress: wantsMount);

        if (!p.IsMounted) _landing = false;

        // C4/C7: mounted inside the ring and going nowhere -> land; a dismount that never takes
        // fails the leg so the director's ladder re-rolls the dropoff
        if (p.IsMounted && insideRing && (arrived || _landing || stalled))
        {
            if (stalled && _landing)
                return new(new NoAction(), BehaviorStatus.Failed, "dropoff not landable");
            _landing = true;
            return new(new Dismount(), BehaviorStatus.Running, "landing");
        }

        if (arrived)
            return new(new NoAction(), BehaviorStatus.Done, "arrived");

        if (stalled)
            return new(new NoAction(), BehaviorStatus.Failed, "stuck");

        if (wantsMount)
            return new(new MountUp(), BehaviorStatus.Running, "mounting");

        // C1: zone override beats flight unlock
        var fly = p.IsMounted && p.CanFly && ctx.ZoneFlightAllowed;
        return new(new GoTo(dropoff, fly, cfg.ArriveTolerance), BehaviorStatus.Running, "moving");
    }

    public void RerollDropoff()
    {
        CurrentDropoff = null;
        _rerolls++;
        _landing = false;
        _stuck.Reset();
    }

    // After a pause elsewhere (a stray fight) the sampler must not read the standstill as a stall
    public void Resume() => _stuck.Reset();

    public void Reset()
    {
        CurrentDropoff = null;
        _rerolls = 0;
        _landing = false;
        _stuck.Reset();
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
