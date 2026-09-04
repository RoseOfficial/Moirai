using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class CollectBehavior(EngageBehavior fight, float npcRange = 5f, int fullCredit = 7) : IBehavior
{
    private bool _combatOff;

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("CollectBehavior requires a fate");
        var live = w.FateById(fate.Id) ?? fate;
        var p = w.Player;

        // B3: event item may not be populated yet — fall through to fighting
        var count = live.EventItemId == 0 ? 0 : w.CountOf(live.EventItemId);

        if (live.Progress >= 100 && count == 0)
            return new(new NoAction(), BehaviorStatus.Done, "collect complete");

        // B1: full batch, or whatever we hold once the fate is complete
        var handInTime = count >= fullCredit || (live.Progress >= 100 && count > 0);
        if (handInTime)
        {
            if (!_combatOff)
            {
                _combatOff = true; // B2: single-owner rule — combat never fights over hand-in targeting
                fight.Reset();     // so the next fight goal turns it back on
                return new(new SetCombat(false, CombatMode.Auto), BehaviorStatus.Running, "combat off for hand-in");
            }
            var npc = w.Interactables.FirstOrDefault(
                i => i.Kind == InteractableKind.ObjectiveNpc && i.FateId == live.Id);
            if (npc is null)
                return new(new Hold(500), BehaviorStatus.Running, "waiting for hand-in npc");
            if (Vector3.Distance(p.Position, npc.Position) > npcRange)
                return new(new GoTo(npc.Position, false, npcRange), BehaviorStatus.Running, "walking to npc");
            return new(new InteractWith(npc.Id), BehaviorStatus.Running, "handing in");
        }

        var pickup = w.Interactables
            .Where(i => i.Kind == InteractableKind.Collectable && i.FateId == live.Id)
            .MinBy(i => Vector3.Distance(p.Position, i.Position));
        if (pickup is not null)
        {
            if (!_combatOff)
            {
                _combatOff = true; // B2
                fight.Reset();
                return new(new SetCombat(false, CombatMode.Auto), BehaviorStatus.Running, "combat off for pickup");
            }
            if (Vector3.Distance(p.Position, pickup.Position) > 3f)
                return new(new GoTo(pickup.Position, false, 3f), BehaviorStatus.Running, "walking to item");
            return new(new InteractWith(pickup.Id), BehaviorStatus.Running, "picking up");
        }

        _combatOff = false; // fight goal: EngageBehavior turns combat back on
        return fight.Tick(w, ctx);
    }

    public void Reset()
    {
        _combatOff = false;
        fight.Reset();
    }
}
