using Moirai.Core;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public sealed class QueueModule(params ModuleDirective[] directives) : IFarmModule
{
    private int _i;
    public ModuleDirective Next(WorldSnapshot w)
        => _i < directives.Length ? directives[_i++] : new FarmHere();
}

public class DirectorTests
{
    private static Director Sut(
        IFarmModule? module = null, DirectorConfig? cfg = null, TravelBehavior? travel = null,
        CompanionUpkeep? companion = null)
    {
        var engage = () => new EngageBehavior(new EngageConfig());
        return new Director(
            module ?? new SingleZoneModule(),
            new SelectionConfig(),
            cfg ?? new DirectorConfig(),
            travel ?? new TravelBehavior(new MovementConfig()),
            kind => kind switch
            {
                FateKind.Collect => new CollectBehavior(engage()),
                FateKind.NpcStart => new NpcStartBehavior(),
                FateKind.Escort => new EscortBehavior(engage()),
                _ => engage(),
            },
            w => new BehaviorContext(null, true, new FixedRandom(0.5, 0.5), new FlatGround()),
            companion);
    }

    private const uint Greens = 4868;

    private static CompanionUpkeep Companion(bool stopWhenOut = false)
        => new(new CompanionConfig { Enabled = true, GreensItemId = Greens, StanceActionId = 7, StopWhenOutOfGreens = stopWhenOut });

    private static Dictionary<uint, int> GreensHeld(int n) => new() { [Greens] = n };

    [Fact] // F9: the companion is summoned in a settled moment (nothing to farm yet)
    public void F9_summons_companion_while_selecting()
    {
        var d = Sut(companion: Companion());
        d.Start();
        var output = d.Tick(TestData.World(items: GreensHeld(3)));
        Assert.IsType<SummonCompanion>(output.Intent);
        Assert.Contains("companion", output.Status);
    }

    [Fact] // F9: never mid-travel, where a summon would only stall the leg
    public void F9_does_not_summon_while_traveling()
    {
        var d = Sut(companion: Companion());
        d.Start();
        var fate = TestData.Fate(id: 1, x: 200, z: 0, radius: 20);
        var withCompanion = TestData.Player(companionSummoned: true, companionTimeLeft: 1800, companionStance: 7);
        d.Tick(TestData.World(player: withCompanion, fates: [fate], items: GreensHeld(3))); // selects
        Assert.Equal(RunPhase.Traveling, d.Phase);

        // the companion drops mid-leg (dismissed on a transition); still no summon until we arrive
        var step = d.Tick(TestData.World(fates: [fate], items: GreensHeld(3)));
        Assert.Equal(RunPhase.Traveling, d.Phase);
        Assert.IsNotType<SummonCompanion>(step.Intent);
    }

    [Fact] // F10: out of greens with the stop option -> typed stop
    public void F10_stops_when_out_of_greens_if_configured()
    {
        var d = Sut(companion: Companion(stopWhenOut: true));
        d.Start();
        var output = d.Tick(TestData.World(items: GreensHeld(0)));
        Assert.IsType<StopRun>(output.Intent);
        Assert.Equal(StopReason.OutOfGreens, d.StoppedBecause);
    }

    [Fact] // F10: without the stop option the run carries on without a companion
    public void F10_continues_without_greens_by_default()
    {
        var d = Sut(companion: Companion());
        d.Start();
        d.Tick(TestData.World(items: GreensHeld(0)));
        Assert.Equal(RunPhase.SelectingFate, d.Phase);
    }

    [Fact]
    public void Idle_until_started()
    {
        var d = Sut();
        Assert.IsType<NoAction>(d.Tick(TestData.World()).Intent);
        d.Start();
        Assert.Equal(RunPhase.SelectingFate, d.Phase);
    }

    [Fact] // happy path: select -> travel -> arrive -> fight -> complete
    public void Full_fate_lifecycle_completes_and_arms_reward_latch()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        var fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60);

        d.Tick(TestData.World(fates: [fate]));                       // selects
        Assert.Equal(RunPhase.Traveling, d.Phase);

        d.Tick(TestData.World(fates: [fate]));                       // first travel tick establishes the dropoff
        var drop = travel.CurrentDropoff!.Value;

        var arrived = TestData.World(player: TestData.Player(x: drop.X, z: drop.Z), fates: [fate]);
        d.Tick(arrived);                                             // on foot at the dropoff: arrival
        Assert.Equal(RunPhase.InFate, d.Phase);

        var ended = TestData.World(now: 20_000, fates: [fate with { Phase = FatePhase.Ended, Progress = 100 }]);
        d.Tick(ended);
        Assert.Equal(1, d.Ledger.Completed);
        Assert.True(d.RewardLatch.IsPending);
        Assert.Equal(RunPhase.SelectingFate, d.Phase);
    }

    [Fact] // D1/D2: death abandons the fate, counts once, resumes
    public void Death_abandons_counts_once_and_resumes()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 10, z: 0);
        d.Tick(TestData.World(fates: [fate]));

        var dead = TestData.World(player: TestData.Player(dead: true), fates: [fate]);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent); // still dead: no double count
        Assert.Equal(1, d.Ledger.Deaths);
        Assert.Equal(1, d.Ledger.Abandoned);

        d.Tick(TestData.World(fates: [fate]));
        Assert.Equal(RunPhase.Traveling, d.Phase); // reselected and moving again
    }

    [Fact] // D1: death cap stops the run
    public void Death_cap_stops_run()
    {
        var d = Sut(cfg: new DirectorConfig { DeathCap = 1 });
        d.Start();
        var dead = TestData.World(player: TestData.Player(dead: true));
        d.Tick(dead);
        Assert.Equal(RunPhase.Stopped, d.Phase);
        Assert.Equal(StopReason.DeathCapReached, d.StoppedBecause);
    }

    [Fact] // D6: zone change waits for the reward latch
    public void D6_zone_change_held_while_reward_pending()
    {
        var d = Sut(module: new QueueModule(new MoveToTerritory(150)));
        d.Start();
        d.RewardLatch.Arm(9);
        var w = TestData.World(fates: [TestData.Fate(id: 9, phase: FatePhase.Ended)]);
        Assert.IsType<Hold>(d.Tick(w).Intent); // latch still pending: fate 9 is in the table
    }

    [Fact] // module stop propagates
    public void Module_stop_stops_the_run()
    {
        var d = Sut(module: new QueueModule(new StopSession(StopReason.SessionComplete, "done")));
        d.Start();
        var output = d.Tick(TestData.World());
        Assert.IsType<StopRun>(output.Intent);
        Assert.Equal(RunPhase.Stopped, d.Phase);
    }

    [Fact] // a behavior finishing while the fate still runs re-dispatches by the fate's current kind
    public void Behavior_done_in_running_fate_redispatches()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var dispatched = new List<FateKind>();
        var d = new Director(
            new SingleZoneModule(),
            new SelectionConfig(),
            new DirectorConfig(),
            travel,
            kind => { dispatched.Add(kind); return new NpcStartBehavior(); },
            w => new BehaviorContext(null, true, new FixedRandom(0.5, 0.5), new FlatGround()));
        d.Start();

        // NpcStart fate: unopened, inside the nearby-override radius so it's picked immediately
        var fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60, kind: FateKind.NpcStart, startTimeEpoch: 0, progress: 0);
        d.Tick(TestData.World(fates: [fate]));                          // selects
        d.Tick(TestData.World(fates: [fate]));                          // travel: establishes dropoff
        var drop = travel.CurrentDropoff!.Value;
        var arrived = TestData.World(player: TestData.Player(x: drop.X, z: drop.Z), fates: [fate]);
        d.Tick(arrived);                                                // arrival dispatches NpcStart
        Assert.Equal(new[] { FateKind.NpcStart }, dispatched);

        // fate opened: same id, now a running battle — NpcStartBehavior reports Done
        var opened = fate with { Kind = FateKind.Battle, StartTimeEpoch = 5_000, Progress = 1 };
        d.Tick(TestData.World(player: TestData.Player(x: drop.X, z: drop.Z), fates: [opened]));
        Assert.Equal(new[] { FateKind.NpcStart, FateKind.Battle }, dispatched);
        Assert.Equal(RunPhase.InFate, d.Phase);
    }

    [Fact] // D3: unexpected combat pauses farming defensively
    public void D3_unexpected_combat_goes_defensive()
    {
        var d = Sut();
        d.Start();
        var w = TestData.World(player: TestData.Player(inCombat: true));
        var set = Assert.IsType<SetCombat>(d.Tick(w).Intent);
        Assert.Equal(CombatMode.Defensive, set.Mode);
    }
}
