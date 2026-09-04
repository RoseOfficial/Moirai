using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class EngageConfig
{
    public float MeleeRange { get; init; } = 2.5f; // C10: larger breaks auto-attack
    public float RangedRange { get; init; } = 8f;
}

public sealed class EngageBehavior(EngageConfig cfg) : IBehavior
{
    private ulong? _sticky;
    private bool _combatOn;

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

        // C5: knocked out of the ring -> walk back to center
        if (!inside && !p.IsMounted)
            return new(new GoTo(live.Position, false, 2f), BehaviorStatus.Running, "re-entering ring");

        // B10: clear a target that isn't ours
        if (p.TargetId is { } tid)
        {
            var held = w.Enemies.FirstOrDefault(e => e.Id == tid);
            if (held is null || held.FateId != live.Id || !held.IsAlive)
                return new(new ClearTarget(), BehaviorStatus.Running, "clearing stray target");
        }

        var chosen = TargetPicker.Choose(w.Enemies, live.Id, _sticky, p.Position);
        if (chosen is null)
            return new(new Hold(500), BehaviorStatus.Running, "no enemies yet");
        _sticky = chosen.Id;

        if (!_combatOn)
        {
            _combatOn = true;
            return new(new SetCombat(true, CombatMode.Auto), BehaviorStatus.Running, "combat on");
        }

        var range = (p.IsMelee ? cfg.MeleeRange : cfg.RangedRange) + chosen.HitboxRadius;
        var dist = Vector3.Distance(p.Position, chosen.Position);

        // B9: while a boss fight runs, the dodge layer owns movement
        var bossInCombat = live.Kind == FateKind.Boss && p.InCombat;
        if (dist > range && !bossInCombat)
            return new(new GoTo(chosen.Position, false, range), BehaviorStatus.Running, "closing");

        if (p.TargetId != chosen.Id)
            return new(new Engage(chosen.Id), BehaviorStatus.Running, "engaging");

        return new(new NoAction(), BehaviorStatus.Running, "fighting");
    }

    public void Reset()
    {
        _sticky = null;
        _combatOn = false;
    }
}
