using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Tests;

public sealed class FixedRandom(params double[] values) : IRandomSource
{
    private int _i;
    public double NextDouble() => values[_i++ % values.Length];
}

public sealed class FlatGround : ILandingResolver
{
    public Vector3? ResolveFloor(Vector3 near) => near with { Y = 0 };
}

public sealed class NoGround : ILandingResolver
{
    public Vector3? ResolveFloor(Vector3 near) => null;
}

public sealed class Hill(float y) : ILandingResolver
{
    public Vector3? ResolveFloor(Vector3 near) => near with { Y = y };
}

public class TravelBehaviorTests
{
    private static BehaviorContext Ctx(FateSnapshot fate, bool flight = true, ILandingResolver? land = null)
        => new(fate, flight, new FixedRandom(0.5, 0.5), land ?? new FlatGround());

    [Fact] // C12: mount only beyond the leg threshold
    public void C12_mounts_only_for_long_legs()
    {
        var fate = TestData.Fate(x: 500, z: 0);
        var sut = new TravelBehavior(new MovementConfig());
        var step = sut.Tick(TestData.World(fates: [fate]), Ctx(fate));
        Assert.IsType<MountUp>(step.Intent);
    }

    [Fact]
    public void Short_legs_walk_instead_of_mounting()
    {
        var fate = TestData.Fate(x: 20, z: 0, radius: 5);
        var sut = new TravelBehavior(new MovementConfig());
        var step = sut.Tick(TestData.World(fates: [fate]), Ctx(fate));
        Assert.IsType<GoTo>(step.Intent);
    }

    [Fact] // C1: zone no-fly override beats CanFly
    public void C1_zone_no_fly_override_disables_flight()
    {
        var fate = TestData.Fate(x: 500, z: 0);
        var sut = new TravelBehavior(new MovementConfig());
        var w = TestData.World(player: TestData.Player(mounted: true, canFly: true), fates: [fate]);
        var step = sut.Tick(w, Ctx(fate, flight: false));
        var go = Assert.IsType<GoTo>(step.Intent);
        Assert.False(go.Fly);
    }

    [Fact] // C16: flight needs the zone's unlock, mounted or not
    public void C16_flight_needs_the_zone_unlock()
    {
        var fate = TestData.Fate(x: 500, z: 0);
        var sut = new TravelBehavior(new MovementConfig());
        var w = TestData.World(player: TestData.Player(mounted: true, canFly: false), fates: [fate]);
        var go = Assert.IsType<GoTo>(sut.Tick(w, Ctx(fate, flight: true)).Intent);
        Assert.False(go.Fly);
    }

    [Fact] // C3: dropoff is a randomized in-ring point on the mesh floor, not the raw center
    public void C3_dropoff_is_randomized_not_center()
    {
        var fate = TestData.Fate(x: 500, z: 500, radius: 60);
        var sut = new TravelBehavior(new MovementConfig());
        sut.Tick(TestData.World(player: TestData.Player(mounted: true), fates: [fate]), Ctx(fate));
        Assert.NotNull(sut.CurrentDropoff);
        Assert.NotEqual(fate.Position, sut.CurrentDropoff!.Value);
        Assert.True(Vector3.Distance(fate.Position, sut.CurrentDropoff.Value) <= fate.Radius);
    }

    [Fact] // C4 (planner half): no landable point after all rerolls -> Failed
    public void No_landable_point_fails_the_behavior()
    {
        var fate = TestData.Fate(x: 500, z: 500);
        var sut = new TravelBehavior(new MovementConfig());
        var step = sut.Tick(TestData.World(fates: [fate]), Ctx(fate, land: new NoGround()));
        Assert.Equal(BehaviorStatus.Failed, step.Status);
    }

    [Fact]
    public void Arrival_dismounts_then_completes()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);

        var mounted = TestData.World(player: TestData.Player(x: 0, z: 0, mounted: true), fates: [fate]);
        sut.Tick(mounted, ctx); // establishes dropoff
        var atDrop = sut.CurrentDropoff!.Value;

        var arrivedMounted = TestData.World(player: TestData.Player(x: atDrop.X, z: atDrop.Z, mounted: true), fates: [fate]);
        Assert.IsType<Dismount>(sut.Tick(arrivedMounted, ctx).Intent);

        var arrivedOnFoot = TestData.World(player: TestData.Player(x: atDrop.X, z: atDrop.Z), fates: [fate]);
        Assert.Equal(BehaviorStatus.Done, sut.Tick(arrivedOnFoot, ctx).Status);
    }

    [Fact] // C3: the ring is a horizontal circle, so a floor-resolved dropoff on a rise is still inside it
    public void C3_arrival_counts_when_floor_sits_far_above_ring_center()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 40);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = new BehaviorContext(fate, true, new FixedRandom(0.5, 1.0), new Hill(30));
        sut.Tick(TestData.World(player: TestData.Player(x: 200, z: 200), fates: [fate]), ctx);
        var drop = sut.CurrentDropoff!.Value;
        Assert.Equal(30f, drop.Y);

        var onRise = TestData.World(player: TestData.Player(x: drop.X, y: drop.Y, z: drop.Z), fates: [fate]);
        Assert.Equal(BehaviorStatus.Done, sut.Tick(onRise, ctx).Status);
    }

    [Fact] // C4: hovering above the dropoff with no movement -> land, then fail once the dismount does not take
    public void C4_hover_above_dropoff_tries_to_land_then_fails()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        sut.Tick(TestData.World(player: TestData.Player(x: 300, z: 0, mounted: true), fates: [fate]), ctx);
        var drop = sut.CurrentDropoff!.Value;

        WorldSnapshot Hover(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: drop.X, y: drop.Y + 10, z: drop.Z, mounted: true), fates: [fate]);

        Assert.IsType<GoTo>(sut.Tick(Hover(0), ctx).Intent);        // anchors the sampler
        Assert.IsType<GoTo>(sut.Tick(Hover(1000), ctx).Intent);     // inside the window: still descending as far as we know
        Assert.IsType<Dismount>(sut.Tick(Hover(2100), ctx).Intent); // stationary past the window: land
        Assert.IsType<Dismount>(sut.Tick(Hover(3000), ctx).Intent); // keep landing while still mounted
        Assert.Equal(BehaviorStatus.Failed, sut.Tick(Hover(4200), ctx).Status);
    }

    [Fact] // C7: mounted and stationary at the dropoff past the window -> fail so the director re-rolls
    public void C7_dismount_that_never_takes_fails_for_reroll()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        sut.Tick(TestData.World(player: TestData.Player(x: 300, z: 0, mounted: true), fates: [fate]), ctx);
        var drop = sut.CurrentDropoff!.Value;

        WorldSnapshot AtDrop(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: drop.X, y: drop.Y + 1, z: drop.Z, mounted: true), fates: [fate]);

        Assert.IsType<Dismount>(sut.Tick(AtDrop(0), ctx).Intent);
        Assert.IsType<Dismount>(sut.Tick(AtDrop(1500), ctx).Intent);
        var step = sut.Tick(AtDrop(2100), ctx);
        Assert.Equal(BehaviorStatus.Failed, step.Status);
        Assert.Contains("land", step.Note);
    }

    [Fact] // C9: a slow final descent near the dropoff is a landing, never a stuck failure
    public void C9_slow_descent_near_dropoff_lands_instead_of_failing()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        sut.Tick(TestData.World(player: TestData.Player(x: 300, z: 0, mounted: true), fates: [fate]), ctx);
        var drop = sut.CurrentDropoff!.Value;

        WorldSnapshot Descending(long ms, float height) => TestData.World(nowMs: ms,
            player: TestData.Player(x: drop.X, y: drop.Y + height, z: drop.Z, mounted: true), fates: [fate]);

        Assert.IsType<GoTo>(sut.Tick(Descending(0, 8f), ctx).Intent);
        Assert.IsType<Dismount>(sut.Tick(Descending(2100, 6.5f), ctx).Intent); // crawling: land rather than re-path
        Assert.IsType<Dismount>(sut.Tick(Descending(3000, 5f), ctx).Intent);

        var grounded = TestData.World(nowMs: 4000, player: TestData.Player(x: drop.X, y: drop.Y, z: drop.Z), fates: [fate]);
        Assert.Equal(BehaviorStatus.Done, sut.Tick(grounded, ctx).Status);
    }

    [Fact] // C8: on foot and wedged en route past the window -> fail so the ladder can escalate
    public void C8_grounded_standstill_en_route_fails()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 45, z: 0, canMount: false), fates: [fate]);

        Assert.IsType<GoTo>(sut.Tick(Wedged(0), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(Wedged(1900), ctx).Intent);
        var step = sut.Tick(Wedged(2100), ctx);
        Assert.Equal(BehaviorStatus.Failed, step.Status);
        Assert.Contains("stuck", step.Note);
    }

    [Fact] // spec 7.2: a mounted standstill outside the ring is stuck, not a landing
    public void Mounted_standstill_en_route_fails_rather_than_dismounting()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Wedged(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, y: 20, z: 0, mounted: true), fates: [fate]);

        Assert.IsType<GoTo>(sut.Tick(Wedged(0), ctx).Intent);
        var step = sut.Tick(Wedged(2100), ctx);
        Assert.Equal(BehaviorStatus.Failed, step.Status);
        Assert.IsNotType<Dismount>(step.Intent);
    }

    [Fact] // spec 7.2: a pause elsewhere (a stray fight) is not a standstill on the leg
    public void Resume_after_a_pause_does_not_read_the_standstill_as_a_stall()
    {
        var fate = TestData.Fate(x: 500, z: 0);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Riding(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 100, z: 0, mounted: true), fates: [fate]);

        Assert.IsType<GoTo>(sut.Tick(Riding(0), ctx).Intent); // anchors the sampler
        sut.Resume();                                          // the leg was paused elsewhere
        Assert.Equal(BehaviorStatus.Running, sut.Tick(Riding(5_000), ctx).Status);
    }

    [Fact] // C12: the mount cast is a legitimate standstill, never a stuck failure
    public void Mount_cast_standstill_is_not_stuck()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Casting(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0), fates: [fate]);

        Assert.IsType<MountUp>(sut.Tick(Casting(0), ctx).Intent);
        Assert.IsType<MountUp>(sut.Tick(Casting(2500), ctx).Intent);
        Assert.IsType<MountUp>(sut.Tick(Casting(5000), ctx).Intent);
    }

    private static readonly Aetheryte NearFarFate = new(5, new Vector3(1900, 0, 0));

    [Fact] // C17: an aetheryte route that beats the direct path by the penalty starts the leg with a teleport
    public void C17_teleports_when_an_aetheryte_route_is_cheaper()
    {
        var fate = TestData.Fate(x: 2000, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var w = TestData.World(fates: [fate], aetherytes: [NearFarFate]);
        var step = sut.Tick(w, Ctx(fate));
        Assert.Equal(5u, Assert.IsType<TeleportTo>(step.Intent).AetheryteId);
        Assert.Equal("teleporting", step.Note);
    }

    [Fact] // C17: a teleport that saves less than the penalty, or one asked for in combat, is not worth it
    public void C17_keeps_the_direct_path_when_teleport_saves_too_little()
    {
        var fate = TestData.Fate(x: 300, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var tooClose = TestData.World(fates: [fate], aetherytes: [new Aetheryte(5, new Vector3(150, 0, 0))]);
        Assert.IsType<MountUp>(sut.Tick(tooClose, Ctx(fate)).Intent);

        var far = TestData.Fate(x: 2000, z: 0, radius: 20);
        var inCombat = TestData.World(player: TestData.Player(inCombat: true), fates: [far], aetherytes: [NearFarFate]);
        Assert.IsType<GoTo>(new TravelBehavior(new MovementConfig()).Tick(inCombat, Ctx(far)).Intent);
    }

    [Fact] // C17: once the player stands at the aetheryte the leg carries on from there
    public void C17_continues_the_leg_after_landing()
    {
        var fate = TestData.Fate(x: 2000, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        Assert.IsType<TeleportTo>(sut.Tick(TestData.World(nowMs: 0, fates: [fate], aetherytes: [NearFarFate]), ctx).Intent);
        Assert.IsType<TeleportTo>(sut.Tick(TestData.World(nowMs: 3000, fates: [fate], aetherytes: [NearFarFate]), ctx).Intent);

        var landed = TestData.World(nowMs: 12_000, player: TestData.Player(x: 1895, z: 3), fates: [fate], aetherytes: [NearFarFate]);
        var step = sut.Tick(landed, ctx);
        Assert.IsType<MountUp>(step.Intent); // a normal leg from the aetheryte: 100 y to ride
        Assert.Equal(BehaviorStatus.Running, sut.Tick(landed with { NowMs = 14_500 }, ctx).Status); // sampler restarted on landing
    }

    [Fact] // C17: a teleport that never lands (no gil, refused) falls back to the direct path without a stall
    public void C17_teleport_that_never_lands_falls_back_to_the_direct_path()
    {
        var fate = TestData.Fate(x: 2000, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Standing(long ms) => TestData.World(nowMs: ms, fates: [fate], aetherytes: [NearFarFate]);

        Assert.IsType<TeleportTo>(sut.Tick(Standing(0), ctx).Intent);
        Assert.IsType<TeleportTo>(sut.Tick(Standing(10_000), ctx).Intent);
        Assert.IsType<TeleportTo>(sut.Tick(Standing(19_000), ctx).Intent);
        var direct = sut.Tick(Standing(20_100), ctx);
        Assert.IsType<MountUp>(direct.Intent);
        Assert.Equal(BehaviorStatus.Running, sut.Tick(Standing(21_000), ctx).Status);
    }

    [Fact] // C15: a mount that never takes is given up on; the leg is walked and the stall sampler starts fresh
    public void C15_mount_that_never_takes_falls_back_to_walking()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Standing(long ms) => TestData.World(nowMs: ms,
            player: TestData.Player(x: 300, z: 0), fates: [fate]);

        Assert.IsType<MountUp>(sut.Tick(Standing(0), ctx).Intent);
        Assert.IsType<MountUp>(sut.Tick(Standing(3000), ctx).Intent);

        var walking = sut.Tick(Standing(6100), ctx);          // budget spent: walk the leg
        Assert.IsType<GoTo>(walking.Intent);
        Assert.False(Assert.IsType<GoTo>(walking.Intent).Fly);
        Assert.Equal(BehaviorStatus.Running, sut.Tick(Standing(7000), ctx).Status); // sampler window restarted at 6100
        var stuck = sut.Tick(Standing(8200), ctx);            // still standing while walking: a real stall
        Assert.Equal(BehaviorStatus.Failed, stuck.Status);
        Assert.Contains("stuck", stuck.Note);
    }

    [Fact] // C15: a mount that took clears the budget, so a knock off the mount mid-leg mounts again
    public void C15_a_mount_that_took_refreshes_the_budget()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);

        Assert.IsType<MountUp>(sut.Tick(TestData.World(nowMs: 0, player: TestData.Player(x: 300, z: 0), fates: [fate]), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(TestData.World(nowMs: 1000, player: TestData.Player(x: 300, z: 0, mounted: true), fates: [fate]), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(TestData.World(nowMs: 4000, player: TestData.Player(x: 200, z: 0, mounted: true), fates: [fate]), ctx).Intent);

        var knockedOff = TestData.World(nowMs: 7000, player: TestData.Player(x: 150, z: 0), fates: [fate]);
        Assert.IsType<MountUp>(sut.Tick(knockedOff, ctx).Intent);
    }

    [Fact] // C15: a walked leg asks for the mount again after the retry window while it is still long
    public void C15_walked_leg_asks_for_the_mount_again_after_the_window()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Walking(long ms, float x) => TestData.World(nowMs: ms,
            player: TestData.Player(x: x, z: 0), fates: [fate]);

        Assert.IsType<MountUp>(sut.Tick(Walking(0, 600), ctx).Intent);
        Assert.IsType<MountUp>(sut.Tick(Walking(3000, 600), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(Walking(6100, 600), ctx).Intent);        // budget spent: walking
        Assert.IsType<GoTo>(sut.Tick(Walking(16_000, 560), ctx).Intent);      // inside the retry window: still walking
        var again = sut.Tick(Walking(26_200, 520), ctx);                      // window over, leg still long: ask again
        Assert.IsType<MountUp>(again.Intent);
        Assert.Equal(BehaviorStatus.Running, sut.Tick(Walking(28_000, 520), ctx).Status); // the new ask is a legitimate standstill
    }

    [Fact] // C15: a walked leg that is nearly there does not stop to mount again
    public void C15_walked_leg_near_the_end_keeps_walking()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var sut = new TravelBehavior(new MovementConfig());
        var ctx = Ctx(fate);
        WorldSnapshot Walking(long ms, float x) => TestData.World(nowMs: ms,
            player: TestData.Player(x: x, z: 0), fates: [fate]);

        Assert.IsType<MountUp>(sut.Tick(Walking(0, 100), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(Walking(6100, 100), ctx).Intent);
        Assert.IsType<GoTo>(sut.Tick(Walking(26_200, 30), ctx).Intent); // window over, but the rest is a short walk
    }
}
