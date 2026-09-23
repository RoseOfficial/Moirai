using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class EngageConfig
{
    public float MeleeRange { get; init; } = 2.5f; // C10: larger breaks auto-attack
    public float RangedRange { get; init; } = 8f;
    public long DodgeSettleMs { get; init; } = 1000; // H3: after danger clears, before navigation takes over again
    public float ReentryDepth { get; init; } = 5f;   // C5: how far inside the edge the walk back in ends
    public float SearchCenterFraction { get; init; } = 0.25f; // B15: this close to the center, as a share of the radius, is there
    public long SearchLookMs { get; init; } = 3000;           // B15: the wait at the center for a wave between spawns
    public float SearchRadiusFraction { get; init; } = 0.6f;  // B15: the patrol circle, as a share of the radius
    public int SearchPoints { get; init; } = 6;               // B15: waypoints around the patrol circle
    public float SearchTolerance { get; init; } = 3f;         // B15: this close to a waypoint is there
    public float SearchStallMove { get; init; } = 2f;         // B15: less than this across the window is not getting anywhere
    public long SearchStallMs { get; init; } = 3000;          // B15: a waypoint not got closer to in this long is given up
}

public sealed class EngageBehavior(EngageConfig cfg) : IBehavior
{
    private readonly RingSearch _search = new(cfg);
    private ulong? _sticky;
    private bool _combatOn;
    private long? _yieldUntilMs; // H2/H3: movement is the dodge layer's until then
    private bool _walking;       // B16: a path of ours may still be running
    private Vector3? _reentry;   // C5: where the walk back into the ring ends, fixed while we are out

    // B16: a path runs until it arrives or is stopped, whatever we ask for next, so every walk of
    // ours is remembered until something stops it
    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var step = Decide(w, ctx);
        if (step.Intent is GoTo)
            _walking = true;
        else if (step.Intent is StopMoving or HandMovementTo { Owner: MovementOwner.Dodge })
            _walking = false;
        return step;
    }

    private BehaviorStep Decide(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("EngageBehavior requires a fate");
        var live = w.FateById(fate.Id) ?? fate;

        if (live.Phase is FatePhase.Ended or FatePhase.Failed || live.Progress >= 100)
        {
            if (_combatOn)
            {
                _combatOn = false;
                return new(new SetCombat(false, CombatMode.Auto), BehaviorStatus.Running, "standing down");
            }
            return new(new NoAction(), BehaviorStatus.Done, "fate finished");
        }

        var p = w.Player;
        var inside = live.Contains(p.Position);

        // C11: sync only once actually inside the ring
        if (inside && !p.IsLevelSynced)
            return new(new SyncLevel(), BehaviorStatus.Running, "syncing");

        // H2/H3: the dodge layer's turn, before anything of ours would walk (the ring re-entry
        // included), so we never path into a marker. Its AI is on with combat, so only then.
        if (w.DodgeReady && _combatOn)
        {
            if (w.Danger)
            {
                _yieldUntilMs = w.NowMs + cfg.DodgeSettleMs;
                return new(new HandMovementTo(MovementOwner.Dodge), BehaviorStatus.Running, "dodging");
            }
            if (_yieldUntilMs is { } until)
            {
                if (w.NowMs < until)
                    return new(new Hold(100), BehaviorStatus.Running, "settling after dodge");
                _yieldUntilMs = null;
                return new(new HandMovementTo(MovementOwner.Navigation), BehaviorStatus.Running, "movement back from dodge");
            }
        }

        // C5: knocked out of the ring -> walk back in just past the edge on our side. The fight is
        // at the edge; the center can be the whole radius away from it.
        if (!inside && !p.IsMounted)
        {
            _reentry ??= ReentryPoint(live, p.Position, ctx);
            return new(new GoTo(_reentry.Value, false, 2f), BehaviorStatus.Running, "re-entering ring");
        }
        _reentry = null;

        // Single-owner rule (§2.3): the combat backend may switch among the fate's enemies as it
        // likes, and whichever one it holds becomes our sticky target. We step in only when the
        // held target is not one of them (B10: another fate's mob, the fate's own friendly NPC,
        // a corpse), and then we replace it rather than clear it, so the backend never re-targets
        // into the gap and the two never trade the target back and forth.
        if (p.TargetId is { } tid
            && w.Enemies.FirstOrDefault(e => e.Id == tid) is { IsAlive: true } held
            && held.FateId == live.Id)
            _sticky = held.Id;

        // B9: while a boss fight runs, the dodge layer owns movement
        var bossInCombat = live.Kind == FateKind.Boss && p.InCombat;

        var chosen = TargetPicker.Choose(w.Enemies, live.Id, _sticky, p.Position);
        if (chosen is null)
        {
            if (p.TargetId is not null)
                return new(new ClearTarget(), BehaviorStatus.Running, "clearing stray target");
            // B9/B15: in combat the fight is here, between kills or out of sight for a moment;
            // repositioning is the dodge layer's, so the search waits for combat to end
            if (p.InCombat)
                return Stand(new Hold(500), "no enemies in view: waiting out combat");
            return _search.Tick(w, live, ctx); // B15: never wait where we landed for a fight out of sight
        }
        _search.Reset(); // the next search starts from the center again
        _sticky = chosen.Id;

        if (!_combatOn)
        {
            _combatOn = true;
            return new(new SetCombat(true, CombatMode.Auto), BehaviorStatus.Running, "combat on");
        }

        // Assert the target before closing distance: while we walk, the backend keeps hitting
        // whatever is held, and a foreign mob must not be it.
        if (p.TargetId != chosen.Id)
            return new(new Engage(chosen.Id), BehaviorStatus.Running, "engaging");

        var range = (p.IsMelee ? cfg.MeleeRange : cfg.RangedRange) + chosen.HitboxRadius;
        var dist = Vector3.Distance(p.Position, chosen.Position);
        if (dist > range && !bossInCombat)
            return new(new GoTo(chosen.Position, false, range), BehaviorStatus.Running, "closing");

        // Re-asserted every tick: the backend switches itself off during a lull, and the switch
        // is a no-op while it reports itself on
        return Stand(new SetCombat(true, CombatMode.Auto), "fighting");
    }

    public void Reset()
    {
        _sticky = null;
        _combatOn = false;
        _yieldUntilMs = null;
        _walking = false;
        _reentry = null;
        _search.Reset();
    }

    // B16: a standstill after a walk of ours stops the path first; until then the fight went on
    // while the character walked to wherever the last path led, a search waypoint across the ring
    // or the spot the target had left
    private BehaviorStep Stand(Intent intent, string note)
        => new(_walking ? new StopMoving() : intent, BehaviorStatus.Running, note);

    // C5: the point on the line from the center to us, the re-entry depth inside the edge (half the
    // radius at most, for a small ring), on the floor when the mesh has one there
    private Vector3 ReentryPoint(FateSnapshot fate, Vector3 from, BehaviorContext ctx)
    {
        var outward = new Vector3(from.X - fate.Position.X, 0, from.Z - fate.Position.Z);
        if (outward.Length() < 0.01f) return fate.Position;
        var depth = MathF.Min(cfg.ReentryDepth, fate.Radius * 0.5f);
        var point = fate.Position + Vector3.Normalize(outward) * (fate.Radius - depth);
        point.Y = from.Y; // the ground near the edge is nearer our height than the center's
        return ctx.Landing.ResolveFloor(point) ?? point;
    }
}
