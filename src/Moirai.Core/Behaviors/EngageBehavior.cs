using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class EngageConfig
{
    public float MeleeRange { get; init; } = 2.5f; // C10: larger breaks auto-attack
    public float RangedRange { get; init; } = 8f;
    public long DodgeSettleMs { get; init; } = 1000; // H3: after danger clears, before navigation takes over again
}

public sealed class EngageBehavior(EngageConfig cfg) : IBehavior
{
    private ulong? _sticky;
    private bool _combatOn;
    private long? _yieldUntilMs; // H2/H3: movement is the dodge layer's until then

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
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

        // C5: knocked out of the ring -> walk back to center
        if (!inside && !p.IsMounted)
            return new(new GoTo(live.Position, false, 2f), BehaviorStatus.Running, "re-entering ring");

        // Single-owner rule (§2.3): the combat backend may switch among the fate's enemies as it
        // likes, and whichever one it holds becomes our sticky target. We step in only when the
        // held target is not one of them (B10: another fate's mob, the fate's own friendly NPC,
        // a corpse), and then we replace it rather than clear it, so the backend never re-targets
        // into the gap and the two never trade the target back and forth.
        if (p.TargetId is { } tid
            && w.Enemies.FirstOrDefault(e => e.Id == tid) is { IsAlive: true } held
            && held.FateId == live.Id)
            _sticky = held.Id;

        var chosen = TargetPicker.Choose(w.Enemies, live.Id, _sticky, p.Position);
        if (chosen is null)
            return p.TargetId is null
                ? new(new Hold(500), BehaviorStatus.Running, "no enemies yet")
                : new(new ClearTarget(), BehaviorStatus.Running, "clearing stray target");
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

        // B9: while a boss fight runs, the dodge layer owns movement
        var bossInCombat = live.Kind == FateKind.Boss && p.InCombat;
        if (dist > range && !bossInCombat)
            return new(new GoTo(chosen.Position, false, range), BehaviorStatus.Running, "closing");

        // Re-asserted every tick: the backend switches itself off during a lull, and the switch
        // is a no-op while it reports itself on
        return new(new SetCombat(true, CombatMode.Auto), BehaviorStatus.Running, "fighting");
    }

    public void Reset()
    {
        _sticky = null;
        _combatOn = false;
        _yieldUntilMs = null;
    }
}
