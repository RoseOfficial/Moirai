using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

// D3: whatever is hitting us that is not the current fate's own enemy is fought where we
// stand, and only that. The rotation runs defensively (attacking the held target alone) so it
// never wanders onto something we did not choose. Nothing is fought from the saddle, where no
// action can be used; the leg carries on and the leash ends that on its own.
public sealed class StrayAggroClear(EngageConfig cfg)
{
    private bool _fighting;
    private ulong? _target;

    public bool Fighting => _fighting;

    // Alive, on us or our companion, and not one of the current fate's enemies: those belong
    // to the fate's behavior, and outside their ring the rotation cannot attack them anyway.
    public static bool IsStray(EnemySnapshot e, uint? currentFateId)
        => e.IsAlive && e.IsAttackingPlayer && (currentFateId is not { } f || e.FateId != f);

    public BehaviorStep? Tick(WorldSnapshot w, uint? currentFateId)
    {
        var p = w.Player;
        var stray = p.IsMounted ? null : Pick(w, currentFateId);
        if (stray is null) return StandDown();
        _target = stray.Id;

        if (!_fighting)
        {
            _fighting = true;
            return new(new SetCombat(true, CombatMode.Defensive), BehaviorStatus.Running, "clearing stray aggro");
        }
        if (p.TargetId != stray.Id)
            return new(new Engage(stray.Id), BehaviorStatus.Running, "engaging stray");

        var range = (p.IsMelee ? cfg.MeleeRange : cfg.RangedRange) + stray.HitboxRadius;
        if (Vector3.Distance(p.Position, stray.Position) > range)
            return new(new GoTo(stray.Position, false, range), BehaviorStatus.Running, "closing on stray");
        // re-asserted every tick, in case the backend dropped it; a no-op while it reports itself on
        return new(new SetCombat(true, CombatMode.Defensive), BehaviorStatus.Running, "fighting stray");
    }

    // Hands the rotation back once nothing is on us; null when there was nothing to hand back.
    public BehaviorStep? StandDown()
    {
        _target = null;
        if (!_fighting) return null;
        _fighting = false;
        return new(new SetCombat(false, CombatMode.Auto), BehaviorStatus.Done, "stray aggro cleared");
    }

    public void Reset()
    {
        _fighting = false;
        _target = null;
    }

    // The one we are on while it is still on us, then the nearest: never swapping between two
    private EnemySnapshot? Pick(WorldSnapshot w, uint? currentFateId)
    {
        EnemySnapshot? nearest = null;
        var best = float.MaxValue;
        foreach (var e in w.Enemies)
        {
            if (!IsStray(e, currentFateId)) continue;
            if (e.Id == _target) return e;
            var d = Vector3.Distance(w.Player.Position, e.Position);
            if (d < best)
            {
                best = d;
                nearest = e;
            }
        }
        return nearest;
    }
}
