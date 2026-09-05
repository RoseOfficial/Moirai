using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

public sealed class NpcStartBehavior : IBehavior
{
    public const long OpenGraceMs = 10_000; // the fate opens a few seconds after the prompt is confirmed

    private double? _stopDistance;
    private bool _interacted;     // B4: only a prompt that follows our own interact is ours
    private long? _confirmedAtMs;

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("NpcStartBehavior requires a fate");
        var live = w.FateById(fate.Id) ?? fate;

        if (!live.IsUnopened)
            return new(new NoAction(), BehaviorStatus.Done, "fate started");
        if (live.Phase is FatePhase.Ended or FatePhase.Failed)
            return new(new NoAction(), BehaviorStatus.Failed, "fate gone");

        // B4: the fate-start yes/no is confirmed only after our own interact with the starter, while
        // the fate is still unopened; the Talk before it is TextAdvance's to advance; a prompt open
        // before we asked for anything is not ours and is left alone
        switch (w.Dialog)
        {
            case DialogKind.YesNo when _interacted:
                _confirmedAtMs = w.NowMs;
                return new(new ConfirmDialog(), BehaviorStatus.Running, "accepting fate start");
            case DialogKind.YesNo:
                return new(new Hold(500), BehaviorStatus.Running, "a prompt is open that is not ours");
            case DialogKind.Talk or DialogKind.Request:
                return new(new Hold(250), BehaviorStatus.Running, "advancing dialogue");
        }

        if (_confirmedAtMs is { } confirmed && w.NowMs - confirmed < OpenGraceMs)
            return new(new Hold(500), BehaviorStatus.Running, "waiting for the fate to open");

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

        _interacted = true;
        _confirmedAtMs = null;
        return new(new InteractWith(npc.Id), BehaviorStatus.Running, "starting fate");
    }

    public void Reset()
    {
        _stopDistance = null;
        _interacted = false;
        _confirmedAtMs = null;
    }
}
