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
}
