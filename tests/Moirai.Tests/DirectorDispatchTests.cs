using Moirai.Core;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class DirectorDispatchTests
{
    // A behavior that never finishes: only the Director can take it off the fate
    private sealed class Idle : IBehavior
    {
        public BehaviorStep Tick(WorldSnapshot w, BehaviorContext ctx) => new(new Hold(100), BehaviorStatus.Running, "idle");
        public void Reset() { }
    }

    [Fact] // B11: the fate's kind changing under a running behavior re-dispatches by the new kind
    public void Kind_change_in_fate_redispatches()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var dispatched = new List<FateKind>();
        var d = new Director(
            new SingleZoneModule(),
            new SelectionConfig(),
            new DirectorConfig(),
            travel,
            kind => { dispatched.Add(kind); return new Idle(); },
            w => new BehaviorContext(null, true, new FixedRandom(0.5, 0.5), new FlatGround()));
        d.Start();

        // a running collect fate inside the nearby-override radius, so it's picked immediately
        var fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60, kind: FateKind.Collect, eventItemId: 900);
        d.Tick(TestData.World(fates: [fate]));                          // selects
        d.Tick(TestData.World(fates: [fate]));                          // travel: establishes dropoff
        var drop = travel.CurrentDropoff!.Value;
        var at = TestData.Player(x: drop.X, z: drop.Z);
        d.Tick(TestData.World(player: at, fates: [fate]));              // arrival dispatches Collect
        Assert.Equal(new[] { FateKind.Collect }, dispatched);

        // the next snapshot classifies the same fate as unopened: the collect loop must yield to NPC-start
        var reclassified = fate with { Kind = FateKind.NpcStart, Phase = FatePhase.Preparing };
        d.Tick(TestData.World(player: at, fates: [reclassified]));
        Assert.Equal(new[] { FateKind.Collect, FateKind.NpcStart }, dispatched);
        Assert.Equal(RunPhase.InFate, d.Phase);
    }
}
