using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class NpcStartBehavior : IBehavior
{
    private double? _stopDistance;

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("NpcStartBehavior requires a fate");
        var live = w.FateById(fate.Id) ?? fate;

        if (!live.IsUnopened)
            return new(new NoAction(), BehaviorStatus.Done, "fate started");
        if (live.Phase is FatePhase.Ended or FatePhase.Failed)
            return new(new NoAction(), BehaviorStatus.Failed, "fate gone");

        // B11: a collect fate opens at its hand-in NPC, which the scan tags as the fate's objective
        // B5: starter may report fate id 0 before opening — accept any starter inside the ring
        var npc = w.Interactables.FirstOrDefault(
                i => i.Kind == InteractableKind.StarterNpc && i.FateId == live.Id)
            ?? w.Interactables.FirstOrDefault(
                i => i.Kind == InteractableKind.ObjectiveNpc && i.FateId == live.Id)
            ?? w.Interactables.FirstOrDefault(
                i => i.Kind == InteractableKind.StarterNpc
                     && live.Contains(i.Position));
        if (npc is null)
            return new(new Hold(500), BehaviorStatus.Running, "waiting for starter npc");

        _stopDistance ??= 1 + ctx.Random.NextDouble() * 4;
        var d = Vector3.Distance(w.Player.Position, npc.Position);
        if (d > (float)_stopDistance)
            return new(new GoTo(npc.Position, false, (float)_stopDistance), BehaviorStatus.Running, "approaching npc");
        if (w.Player.IsMounted)
            return new(new Dismount(), BehaviorStatus.Running, "dismounting for npc");

        // B4 lives in the executor: it confirms only the fate-start dialog and rejects all others
        return new(new InteractWith(npc.Id), BehaviorStatus.Running, "starting fate");
    }

    public void Reset() => _stopDistance = null;
}
