using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Core.Behaviors;

// B15: none of the fate's enemies is in view. The game only lists what stands near the character,
// so in a wide ring the fight can be out of sight from where we landed. Look from the center
// first, where the whole ring is closest, and wait there a moment for a wave between spawns; then
// walk a circle inside the ring, starting on the side we have not seen, until something shows up.
public sealed class RingSearch(EngageConfig cfg)
{
    private const int CenterLeg = -1;

    private readonly StuckDetector _stall = new(cfg.SearchStallMove, cfg.SearchStallMs);
    private int _leg = CenterLeg;
    private float? _startAngle;  // the patrol's first waypoint: opposite where the search began
    private Vector3? _point;     // the current waypoint, resolved to the floor once
    private long? _lookUntilMs;  // the wait at the center

    public BehaviorStep Tick(WorldSnapshot w, FateSnapshot fate, BehaviorContext ctx)
    {
        var p = w.Player;
        _startAngle ??= Bearing(fate.Position, p.Position) + MathF.PI;

        if (_leg == CenterLeg)
        {
            var near = MathF.Max(cfg.SearchTolerance, fate.Radius * cfg.SearchCenterFraction);
            if (Geometry.HorizontalDistance(p.Position, fate.Position) <= near)
            {
                _lookUntilMs ??= w.NowMs + cfg.SearchLookMs;
                if (w.NowMs < _lookUntilMs)
                    return new(new Hold(250), BehaviorStatus.Running, "no enemies in view: looking from the center");
                NextLeg();
            }
            else
            {
                _point ??= Floor(fate.Position, ctx);
                if (!Stalled(w))
                    return new(new GoTo(_point.Value, false, near), BehaviorStatus.Running, "no enemies in view: heading to the center");
                NextLeg(); // the center cannot be reached: walk the circle instead
            }
        }

        _point ??= Waypoint(fate, ctx);
        if (Geometry.HorizontalDistance(p.Position, _point.Value) <= cfg.SearchTolerance || Stalled(w))
        {
            NextLeg();
            _point = Waypoint(fate, ctx);
        }
        return new(new GoTo(_point.Value, false, cfg.SearchTolerance), BehaviorStatus.Running, "no enemies in view: searching the ring");
    }

    public void Reset()
    {
        _leg = CenterLeg;
        _startAngle = null;
        _point = null;
        _lookUntilMs = null;
        _stall.Reset();
    }

    private bool Stalled(WorldSnapshot w) => _stall.Sample(w.Player.Position, w.NowMs, suppress: false);

    private void NextLeg()
    {
        _leg = _leg == CenterLeg ? 0 : (_leg + 1) % cfg.SearchPoints;
        _point = null;
        _stall.Reset();
    }

    private Vector3 Waypoint(FateSnapshot fate, BehaviorContext ctx)
    {
        var angle = _startAngle!.Value + _leg * MathF.Tau / cfg.SearchPoints;
        var r = fate.Radius * cfg.SearchRadiusFraction;
        return Floor(fate.Position + new Vector3(MathF.Cos(angle) * r, 0, MathF.Sin(angle) * r), ctx);
    }

    // A walking destination: the floor under the point when the mesh has one there, else the point itself
    private static Vector3 Floor(Vector3 point, BehaviorContext ctx) => ctx.Landing.ResolveFloor(point) ?? point;

    private static float Bearing(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        return dx * dx + dz * dz < 0.0001f ? 0f : MathF.Atan2(dz, dx);
    }
}
