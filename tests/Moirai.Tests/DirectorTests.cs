using System.Numerics;
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

    [Fact] // D1/D2: a death on the road abandons the fate, counts once, and the fate is fair game again (D9)
    public void Death_abandons_counts_once_and_resumes()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 500, z: 0);
        d.Tick(TestData.World(fates: [fate]));

        var dead = TestData.World(player: TestData.Player(dead: true), fates: [fate]);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent); // still dead: no double count
        Assert.Equal(1, d.Ledger.Deaths);
        Assert.Equal(1, d.Ledger.Abandoned);

        d.Tick(TestData.World(fates: [fate]));
        Assert.Equal(RunPhase.Traveling, d.Phase); // reselected and moving again
    }

    [Fact] // D9: a fate we died in is not selected again this session, even from inside its ring
    public void D9_fate_that_killed_us_is_not_reselected()
    {
        var d = InFate(out var fate, out var at);
        var dead = TestData.World(player: at with { IsDead = true }, fates: [fate]);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent);

        // revived on the spot: the nearby override would otherwise take it straight back
        var revived = TestData.World(player: at, fates: [fate]);
        Assert.Equal("no eligible fates", d.Tick(revived).Status);
        Assert.Equal(RunPhase.SelectingFate, d.Phase);

        var other = TestData.Fate(id: 2, x: 500, z: 0);
        Assert.Equal("selected fate 2", d.Tick(TestData.World(player: at, fates: [fate, other])).Status);
    }

    [Fact] // D9: dying inside the ring before the leg is over counts the same as dying in the fight
    public void D9_death_inside_ring_before_arrival_condemns_the_fate()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60); // the ring covers the origin
        d.Tick(TestData.World(fates: [fate]));
        Assert.Equal(RunPhase.Traveling, d.Phase);

        var dead = TestData.World(player: TestData.Player(dead: true), fates: [fate]);
        Assert.IsType<AcceptReturn>(d.Tick(dead).Intent);

        Assert.Equal("no eligible fates", d.Tick(TestData.World(fates: [fate])).Status);
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

    [Fact] // D3: unexpected combat with a stray on us goes defensive and targets it
    public void D3_unexpected_combat_goes_defensive()
    {
        var d = Sut();
        d.Start();
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var w = TestData.World(player: TestData.Player(inCombat: true), enemies: [stray]);
        var set = Assert.IsType<SetCombat>(d.Tick(w).Intent);
        Assert.Equal(CombatMode.Defensive, set.Mode);
        Assert.Equal(9uL, Assert.IsType<Engage>(d.Tick(w).Intent).TargetId);
    }

    [Fact] // D3: combat with nothing we can fight (a lingering flag, a fate mob outside its ring) carries on
    public void D3_combat_with_nothing_to_fight_carries_on()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 200, z: 0);
        var w = TestData.World(player: TestData.Player(inCombat: true), fates: [fate]);
        Assert.Equal("selected fate 1", d.Tick(w).Status);
        Assert.Equal(RunPhase.Traveling, d.Phase);
    }

    [Fact] // D3 mounted variant: keep riding; nothing can be fought from the saddle and the leash ends it
    public void D3_mounted_stray_aggro_keeps_traveling()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 500, z: 0);
        d.Tick(TestData.World(fates: [fate])); // selects
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var riding = TestData.World(player: TestData.Player(mounted: true, inCombat: true), fates: [fate], enemies: [stray]);
        Assert.IsType<GoTo>(d.Tick(riding).Intent);
    }

    [Fact] // D3: after the stray is down the rotation stands down and the leg resumes
    public void D3_stray_clear_stands_down_then_resumes_travel()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 500, z: 0);
        d.Tick(TestData.World(fates: [fate])); // selects
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var attacked = TestData.World(player: TestData.Player(inCombat: true), fates: [fate], enemies: [stray]);
        Assert.IsType<SetCombat>(d.Tick(attacked).Intent);
        Assert.IsType<Engage>(d.Tick(attacked).Intent);

        var calm = TestData.World(fates: [fate]);
        var down = Assert.IsType<SetCombat>(d.Tick(calm).Intent);
        Assert.False(down.Enabled);
        Assert.IsType<MountUp>(d.Tick(calm).Intent);
    }

    [Fact] // spec 7.2: standing still for a stray fight is not a stuck leg
    public void D3_stray_fight_on_a_short_leg_is_not_a_stall()
    {
        var d = Sut();
        d.Start();
        var fate = TestData.Fate(id: 1, x: 20, z: 0, radius: 5); // a walking leg
        d.Tick(TestData.World(nowMs: 0, fates: [fate]));         // selects
        Assert.IsType<GoTo>(d.Tick(TestData.World(nowMs: 100, fates: [fate])).Intent);

        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var attacked = TestData.World(nowMs: 200, player: TestData.Player(inCombat: true), fates: [fate], enemies: [stray]);
        d.Tick(attacked);
        d.Tick(attacked);

        Assert.False(Assert.IsType<SetCombat>(d.Tick(TestData.World(nowMs: 6_000, fates: [fate])).Intent).Enabled);
        var resumed = d.Tick(TestData.World(nowMs: 6_100, fates: [fate]));
        Assert.IsType<GoTo>(resumed.Intent);
        Assert.Equal("moving", resumed.Status);
    }

    private static Director InFate(out FateSnapshot fate, out PlayerSnapshot at)
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60);
        d.Tick(TestData.World(fates: [fate]));                          // selects
        d.Tick(TestData.World(fates: [fate]));                          // travel: establishes dropoff
        var drop = travel.CurrentDropoff!.Value;
        at = TestData.Player(x: drop.X, z: drop.Z, synced: true);
        d.Tick(TestData.World(player: at, fates: [fate]));              // arrives
        Assert.Equal(RunPhase.InFate, d.Phase);
        return d;
    }

    [Fact] // D3: inside the fate, a stray on us with no fate enemy on us is cleared before the fate resumes
    public void D3_in_fate_stray_is_cleared_when_no_fate_enemy_is_on_us()
    {
        var d = InFate(out var fate, out var at);
        var stray = TestData.Enemy(id: 9, fateId: 0, x: at.Position.X + 1, z: at.Position.Z, attacksMe: true);
        var w = TestData.World(player: at with { InCombat = true }, fates: [fate], enemies: [stray]);
        var set = Assert.IsType<SetCombat>(d.Tick(w).Intent);
        Assert.Equal(CombatMode.Defensive, set.Mode);
        Assert.Equal(9uL, Assert.IsType<Engage>(d.Tick(w).Intent).TargetId);
    }

    [Fact] // D3: a fate enemy on us outranks the stray; the fate's own behavior fights in its own mode
    public void D3_in_fate_fate_enemy_on_us_outranks_the_stray()
    {
        var d = InFate(out var fate, out var at);
        var stray = TestData.Enemy(id: 9, fateId: 0, x: at.Position.X + 1, z: at.Position.Z, attacksMe: true);
        var ours = TestData.Enemy(id: 7, fateId: 1, x: at.Position.X + 2, z: at.Position.Z, attacksMe: true);
        var w = TestData.World(player: at with { InCombat = true }, fates: [fate], enemies: [stray, ours]);
        var set = Assert.IsType<SetCombat>(d.Tick(w).Intent);
        Assert.Equal(CombatMode.Auto, set.Mode);
        Assert.Equal(7uL, Assert.IsType<Engage>(d.Tick(w).Intent).TargetId);
    }

    [Fact] // D3: when the stray is down, the fate's behavior starts over so its own rotation mode comes back
    public void D3_in_fate_behavior_restarts_after_a_stray_clear()
    {
        var d = InFate(out var fate, out var at);
        var ours = TestData.Enemy(id: 7, fateId: 1, x: at.Position.X + 2, z: at.Position.Z, attacksMe: true);
        var fighting = TestData.World(player: at with { InCombat = true }, fates: [fate], enemies: [ours]);
        Assert.Equal(CombatMode.Auto, Assert.IsType<SetCombat>(d.Tick(fighting).Intent).Mode);
        Assert.IsType<Engage>(d.Tick(fighting).Intent);

        // the wave is down and a wandering mob is on us
        var stray = TestData.Enemy(id: 9, fateId: 0, x: at.Position.X + 1, z: at.Position.Z, attacksMe: true);
        var strayed = TestData.World(player: at with { InCombat = true }, fates: [fate], enemies: [stray]);
        Assert.Equal(CombatMode.Defensive, Assert.IsType<SetCombat>(d.Tick(strayed).Intent).Mode);
        d.Tick(strayed); // engages the stray

        // the stray is down and the next wave is up: stand down, then the fate's own mode again
        var nextWave = TestData.World(player: at with { InCombat = true }, fates: [fate], enemies: [ours]);
        Assert.False(Assert.IsType<SetCombat>(d.Tick(nextWave).Intent).Enabled);
        var back = Assert.IsType<SetCombat>(d.Tick(nextWave).Intent);
        Assert.True(back.Enabled);
        Assert.Equal(CombatMode.Auto, back.Mode);
    }

    [Fact] // D3: in-fate combat past the ring edge hands control to the engage behavior, which walks back in
    public void D3_in_fate_combat_outside_ring_reenters_instead_of_pausing()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();

        var fate = TestData.Fate(id: 1, x: 10, z: 0, radius: 60);
        d.Tick(TestData.World(fates: [fate]));                          // selects
        d.Tick(TestData.World(fates: [fate]));                          // travel: establishes dropoff
        var drop = travel.CurrentDropoff!.Value;
        d.Tick(TestData.World(player: TestData.Player(x: drop.X, z: drop.Z), fates: [fate]));
        Assert.Equal(RunPhase.InFate, d.Phase);

        var knockedOut = TestData.World(player: TestData.Player(x: 100, z: 0, inCombat: true, synced: true), fates: [fate]);
        var output = d.Tick(knockedOut);
        var go = Assert.IsType<GoTo>(output.Intent);
        Assert.Equal(fate.Position, go.Destination);
    }

    [Fact] // spec 7.2: a travel standstill climbs the ladder until a new dropoff is rolled
    public void Travel_standstill_climbs_ladder_to_a_new_dropoff()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var random = new FixedRandom(0.5, 0.5, 0.1, 0.9); // one instance: the re-roll must draw the next values
        var d = new Director(new SingleZoneModule(), new SelectionConfig(), new DirectorConfig(), travel,
            _ => new EngageBehavior(new EngageConfig()),
            w => new BehaviorContext(null, true, random, new FlatGround()));
        d.Start();
        var fate = TestData.Fate(id: 1, x: 100, z: 0, radius: 60);
        d.Tick(TestData.World(fates: [fate])); // selects
        d.Tick(TestData.World(fates: [fate])); // establishes the dropoff
        var drop = travel.CurrentDropoff!.Value;

        WorldSnapshot Hover(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: drop.X, y: drop.Y + 10, z: drop.Z, mounted: true), fates: [fate]);

        var statuses = new List<string>();
        for (long ms = 0; ms <= 9000; ms += 300)
            statuses.Add(d.Tick(Hover(ms)).Status);

        Assert.Contains("recovery: re-path", statuses);
        Assert.Contains("recovery: new dropoff", statuses);
        Assert.Equal(RunPhase.Traveling, d.Phase);
        Assert.NotEqual(drop, travel.CurrentDropoff!.Value);
    }

    // Ticks a wedged leg every 100 ms and keeps every output, so a test can read the ladder's timeline
    private static List<(long Ms, DirectorOutput Out)> Drive(Director d, Func<long, WorldSnapshot> at, long endMs)
    {
        var outs = new List<(long, DirectorOutput)>();
        for (long ms = 0; ms <= endMs; ms += 100)
            outs.Add((ms, d.Tick(at(ms))));
        return outs;
    }

    [Fact] // C13: the re-path rung stops the running path, so the next tick issues the leg afresh
    public void C13_repath_rung_stops_the_path_before_the_leg_is_reissued()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        var fate = TestData.Fate(id: 1, x: 100, z: 0, radius: 20);
        d.Tick(TestData.World(fates: [fate])); // selects
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0, canMount: false), fates: [fate]);

        Assert.IsType<GoTo>(d.Tick(Wedged(0)).Intent);
        var stuck = d.Tick(Wedged(2100));
        Assert.IsType<StopMoving>(stuck.Intent);
        Assert.Equal("recovery: re-path", stuck.Status);
        var again = Assert.IsType<GoTo>(d.Tick(Wedged(2200)).Intent);
        Assert.Equal(travel.CurrentDropoff!.Value, again.Destination);
    }

    [Fact] // C14: the escape rung flies up and holds it for its window before the leg resumes
    public void C14_vertical_escape_is_held_before_travel_resumes()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        var fate = TestData.Fate(id: 1, x: 100, z: 0, radius: 20);
        d.Tick(TestData.World(fates: [fate])); // selects
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, y: 20, z: 0, mounted: true, canFly: true), fates: [fate]);

        var outs = Drive(d, Wedged, 14_000);
        var (begin, first) = outs.First(o => o.Out.Status == "recovery: flying up");
        var up = Assert.IsType<GoTo>(first.Intent);
        Assert.True(up.Fly);
        Assert.Equal(30f, up.Destination.Y);

        foreach (var (ms, held) in outs.Where(o => o.Ms > begin && o.Ms < begin + 1500))
            Assert.Equal("recovery: flying up", held.Status);

        var (_, resumed) = outs.First(o => o.Ms >= begin + 1500);
        Assert.Equal("moving", resumed.Status);
        Assert.Equal(travel.CurrentDropoff!.Value, Assert.IsType<GoTo>(resumed.Intent).Destination);
        Assert.Equal(RunPhase.Traveling, d.Phase);
    }

    [Fact] // C8: on foot the escape is a sideways nudge with a jump, held for the same window
    public void C8_ground_escape_nudges_sideways_with_a_jump()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        var fate = TestData.Fate(id: 1, x: 100, z: 0, radius: 20);
        d.Tick(TestData.World(fates: [fate])); // selects
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0, canMount: false), fates: [fate]);

        var outs = Drive(d, Wedged, 14_000);
        var (begin, first) = outs.First(o => o.Out.Status == "recovery: nudging sideways");
        var nudge = Assert.IsType<GoTo>(first.Intent);
        Assert.False(nudge.Fly);
        Assert.Equal(5f, Geometry.HorizontalDistance(nudge.Destination, new Vector3(300, 0, 0)), 3);

        var (_, second) = outs.First(o => o.Ms > begin);
        Assert.IsType<Jump>(second.Intent);
        Assert.Equal("recovery: jumping clear", second.Status);

        foreach (var (_, held) in outs.Where(o => o.Ms > begin + 100 && o.Ms < begin + 1500))
        {
            Assert.Equal("recovery: nudging sideways", held.Status);
            Assert.Equal(nudge.Destination, Assert.IsType<GoTo>(held.Intent).Destination);
        }

        var (_, resumed) = outs.First(o => o.Ms >= begin + 1500);
        Assert.Equal("moving", resumed.Status);
        Assert.Equal(travel.CurrentDropoff!.Value, Assert.IsType<GoTo>(resumed.Intent).Destination);
    }

    // Ticks every 100 ms until an output satisfies the predicate; returns the time it did
    private static long DriveUntil(Director d, Func<long, WorldSnapshot> at, Func<DirectorOutput, bool> done, long fromMs, long maxMs = 60_000)
    {
        for (var ms = fromMs; ms <= fromMs + maxMs; ms += 100)
            if (done(d.Tick(at(ms)))) return ms;
        throw new Xunit.Sdk.XunitException("the condition never came");
    }

    [Fact] // D10: an exhausted ladder abandons the fate, skips it for the session, and selection goes on
    public void D10_exhausted_ladder_abandons_the_fate_and_moves_on()
    {
        var d = Sut();
        d.Start();
        var near = TestData.Fate(id: 1, x: 100, z: 0, radius: 20);
        var far = TestData.Fate(id: 2, x: -900, z: 0, radius: 20);
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0, canMount: false), fates: [near, far]);

        Assert.Equal("selected fate 1", d.Tick(Wedged(0)).Status);
        var gaveUp = DriveUntil(d, Wedged, o => o.Status.StartsWith("recovery exhausted"), 100);

        Assert.Equal(1, d.Ledger.Abandoned);
        Assert.Equal(SkipReason.Unreachable, d.Skips.Reason(1));
        Assert.Equal(RunPhase.SelectingFate, d.Phase);
        Assert.Equal("selected fate 2", d.Tick(Wedged(gaveUp + 100)).Status);
    }

    [Fact] // D10: three exhaustions in a row without moving between them is a wedged character: stop
    public void D10_three_exhaustions_in_place_stop_the_run()
    {
        var d = Sut();
        d.Start();
        var fates = new[]
        {
            TestData.Fate(id: 1, x: 100, z: 0, radius: 20),
            TestData.Fate(id: 2, x: -900, z: 0, radius: 20),
            TestData.Fate(id: 3, x: 0, z: 900, radius: 20),
        };
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0, canMount: false), fates: fates);

        var ms = 0L;
        for (var i = 0; i < 3; i++)
            ms = DriveUntil(d, Wedged, o => o.Status.StartsWith("recovery exhausted"), ms + 100);

        Assert.Equal(RunPhase.Stopped, d.Phase);
        Assert.Equal(StopReason.StuckExhausted, d.StoppedBecause);
        Assert.Equal(3, d.Ledger.Abandoned);
    }

    [Fact] // D10: a character that moved between exhaustions is not wedged; the fates were unreachable
    public void D10_moving_between_exhaustions_keeps_the_run_going()
    {
        var d = Sut();
        d.Start();
        var fates = new[]
        {
            TestData.Fate(id: 1, x: 100, z: 0, radius: 20),
            TestData.Fate(id: 2, x: -900, z: 0, radius: 20),
            TestData.Fate(id: 3, x: 0, z: 900, radius: 20),
        };
        var x = 300f;
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: x, z: 0, canMount: false), fates: fates);

        var ms = 0L;
        for (var i = 0; i < 3; i++)
        {
            ms = DriveUntil(d, Wedged, o => o.Status.StartsWith("recovery exhausted"), ms + 100);
            x += 50; // a different spot each time
        }

        Assert.NotEqual(RunPhase.Stopped, d.Phase);
        Assert.Equal(3, d.Ledger.Abandoned);
    }

    [Fact] // C8/C1: mounted without flight, or in a no-fly zone, the escape is the ground one
    public void C8_mounted_without_flight_uses_the_ground_escape()
    {
        var travel = new TravelBehavior(new MovementConfig());
        var d = Sut(travel: travel);
        d.Start();
        var fate = TestData.Fate(id: 1, x: 100, z: 0, radius: 20);
        d.Tick(TestData.World(fates: [fate])); // selects
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, y: 20, z: 0, mounted: true, canFly: false), fates: [fate]);

        var outs = Drive(d, Wedged, 14_000);
        Assert.DoesNotContain(outs, o => o.Out.Status == "recovery: flying up");
        Assert.Contains(outs, o => o.Out.Status == "recovery: nudging sideways");
    }
}
