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
    private bool _landing;      // a dismount has been issued at this dropoff and has not taken yet
    private long? _mountAskedMs; // when the current unbroken run of mount requests began
    private bool _walkLeg;      // C15: the mount never took within its budget; the leg is walked
    private long _walkSinceMs;  // C15: when the walked leg began, for the retry window
    private bool _teleportDecided; // C17: the aetheryte question is asked once per leg
    private Aetheryte? _teleport;  // the aetheryte the leg starts from, until we stand there
    private long _teleportAskedMs;

    public Vector3? CurrentDropoff { get; private set; }
    public bool RerollsExhausted => _rerolls >= MaxRerolls;
    public bool Teleporting => _teleport is not null; // C17: a teleport is asked for and has not landed

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("TravelBehavior requires a fate");
        if (fate.Phase is FatePhase.Ended or FatePhase.Failed)
            return new(new NoAction(), BehaviorStatus.Failed, "fate gone");

        CurrentDropoff ??= PickDropoff(fate, ctx);
        if (CurrentDropoff is not { } dropoff)
            return new(new NoAction(), BehaviorStatus.Failed, "no landable point");

        var p = w.Player;

        // C17: an attuned aetheryte whose route beats the direct path by the penalty starts the leg
        // with a teleport (the ranking's A12 cost model, honored on the ground). Decided once per
        // leg, never in combat; held until we stand at the aetheryte or the teleport is given up on.
        if (!_teleportDecided)
        {
            _teleportDecided = true;
            if (!p.InCombat && CheaperViaAetheryte(w, dropoff) is { } via)
            {
                _teleport = via;
                _teleportAskedMs = w.NowMs;
            }
        }
        if (_teleport is { } aetheryte)
        {
            var landed = Geometry.HorizontalDistance(p.Position, aetheryte.Position) <= cfg.TeleportArriveRadius;
            if (!landed && w.NowMs - _teleportAskedMs < cfg.TeleportTimeoutMs)
                return new(new TeleportTo(aetheryte.Id), BehaviorStatus.Running, "teleporting");
            _teleport = null; // landed, or given up on: the leg goes on from wherever we stand
            _stuck.Reset();
        }

        var distToDrop = Vector3.Distance(p.Position, dropoff);
        var insideRing = fate.Contains(p.Position);
        var overDropoff = Geometry.HorizontalDistance(p.Position, dropoff) <= cfg.ArriveTolerance
                          && MathF.Abs(p.Position.Y - dropoff.Y) <= cfg.ArriveVerticalTolerance;
        var arrived = overDropoff && insideRing;

        // C12: mount only when the leg is worth it and mounting is legal
        if (p.IsMounted) _walkLeg = false; // a mount that took earns a fresh budget
        // C15: a walked leg asks again once the retry window is over, so a mount that failed once
        // (a no-mount spot, an ask that landed mid-movement) is not a walk across the map
        if (_walkLeg && w.NowMs - _walkSinceMs >= cfg.MountRetryMs) _walkLeg = false;
        var wantsMount = !p.IsMounted && p.CanMount && !p.InCombat && distToDrop > cfg.MountLegThreshold && !_walkLeg;

        // C15: a mount that never takes (no mount owned, a no-mount spot the game refuses) is
        // asked for only so long; then the leg is walked and the stall sampler starts fresh
        if (wantsMount)
        {
            _mountAskedMs ??= w.NowMs;
            if (w.NowMs - _mountAskedMs >= cfg.MountAttemptMs)
            {
                _walkLeg = true;
                _walkSinceMs = w.NowMs;
                wantsMount = false;
                _stuck.Reset();
            }
        }
        else
        {
            _mountAskedMs = null;
        }

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

    // Spec 7.2 rung 4: start the leg over from an aetheryte. The teleport is held like a C17 one.
    public void RestartVia(Aetheryte aetheryte, long nowMs)
    {
        Reset();
        _teleportDecided = true;
        _teleport = aetheryte;
        _teleportAskedMs = nowMs;
    }

    public void Reset()
    {
        CurrentDropoff = null;
        _rerolls = 0;
        _landing = false;
        _mountAskedMs = null;
        _walkLeg = false;
        _walkSinceMs = 0;
        _teleportDecided = false;
        _teleport = null;
        _stuck.Reset();
    }

    // C17/A12: the cheapest aetheryte to start from, or null when the direct path wins
    private Aetheryte? CheaperViaAetheryte(WorldSnapshot w, Vector3 dropoff)
    {
        Aetheryte? best = null;
        var bestCost = Vector3.Distance(w.Player.Position, dropoff);
        foreach (var a in w.Aetherytes)
        {
            var cost = Vector3.Distance(a.Position, dropoff) + cfg.TeleportPenalty;
            if (cost < bestCost)
            {
                bestCost = cost;
                best = a;
            }
        }
        return best;
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
