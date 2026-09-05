using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

// Spec 7.2 rung 3, held for a window so the leg cannot overwrite it on the next tick: mounted
// with flight, climb straight up (C14); otherwise walk a few yalms sideways and jump while
// doing so (C8). When the window is over the leg resumes with its stall sampler re-anchored.
public sealed class EscapeManeuver(long holdMs = 1500, float climb = 10f, float nudge = 5f)
{
    private long _until;
    private int _ticks;
    private GoTo? _move;
    private bool _grounded;

    public bool Active { get; private set; }

    public BehaviorStep Begin(WorldSnapshot w, BehaviorContext ctx)
    {
        var p = w.Player;
        Active = true;
        _until = w.NowMs + holdMs;
        _ticks = 0;
        _grounded = !(p.IsMounted && p.CanFly && ctx.ZoneFlightAllowed);
        if (!_grounded)
        {
            _move = new GoTo(p.Position with { Y = p.Position.Y + climb }, true, 2f);
            return Step(_move, "recovery: flying up");
        }
        var angle = ctx.Random.NextDouble() * Math.PI * 2;
        var raw = p.Position + new Vector3((float)Math.Cos(angle) * nudge, 0, (float)Math.Sin(angle) * nudge);
        _move = new GoTo(ctx.Landing.ResolveFloor(raw) ?? raw, false, 1f);
        return Step(_move, "recovery: nudging sideways");
    }

    // The held step; null once the window is over
    public BehaviorStep? Tick(WorldSnapshot w)
    {
        if (!Active) return null;
        if (w.NowMs >= _until)
        {
            Active = false;
            return null;
        }
        _ticks++;
        if (_grounded && _ticks == 1)
            return new(new Jump(), BehaviorStatus.Running, "recovery: jumping clear");
        return Step(_move!, _grounded ? "recovery: nudging sideways" : "recovery: flying up");
    }

    public void Reset() => Active = false;

    private static BehaviorStep Step(GoTo move, string note) => new(move, BehaviorStatus.Running, note);
}
