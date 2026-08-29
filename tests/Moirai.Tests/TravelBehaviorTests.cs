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
}
