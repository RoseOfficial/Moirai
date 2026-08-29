using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Behaviors;

// B6: follow with hysteresis so we don't stutter-step behind the escortee
public sealed class EscortFollow(float start = 5f, float stop = 2.5f)
{
    private bool _moving;

    public Intent Decide(Vector3 npc, Vector3 player)
    {
        var d = Vector3.Distance(npc, player);
        if (_moving)
        {
            if (d <= stop)
            {
                _moving = false;
                return new NoAction();
            }
            return new GoTo(npc, false, stop);
        }
        if (d >= start)
        {
            _moving = true;
            return new GoTo(npc, false, stop);
        }
        return new NoAction();
    }
}

public sealed class EscortBehavior(EngageBehavior fight) : IBehavior
{
    private readonly EscortFollow _follow = new();

    public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx)
    {
        var fate = ctx.Fate ?? throw new InvalidOperationException("EscortBehavior requires a fate");
        var live = w.FateById(fate.Id) ?? fate;

        if (live.Phase is FatePhase.Ended or FatePhase.Failed || live.Progress >= 100)
            return fight.Tick(w, ctx); // shares stand-down/done handling

        if (w.Enemies.Any(e => e.IsAlive && e.FateId == live.Id))
            return fight.Tick(w, ctx);

        var escortee = w.Interactables.FirstOrDefault(
            i => i.Kind == InteractableKind.ObjectiveNpc && i.FateId == live.Id);
        if (escortee is null)
            return new(new Hold(500), BehaviorStatus.Running, "waiting for escortee");

        return new(_follow.Decide(escortee.Position, w.Player.Position), BehaviorStatus.Running, "following");
    }

    public void Reset() => fight.Reset();
}
