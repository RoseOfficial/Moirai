using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class FateRankerTests
{
    private static readonly SelectionConfig Cfg = new();

    [Fact] // ladder head: higher progress wins
    public void Higher_progress_outranks()
    {
        var w = TestData.World(fates: [TestData.Fate(id: 1, progress: 10, x: 400, z: 0), TestData.Fate(id: 2, progress: 60, x: 500, z: 500)]);
        Assert.Equal(2u, FateRanker.PickBest(w, Cfg)!.Id);
    }

    [Fact] // A6
    public void A6_bonus_outranks_when_progress_ties()
    {
        var w = TestData.World(fates: [TestData.Fate(id: 1, x: 400, z: 400), TestData.Fate(id: 2, bonus: true, x: 500, z: 500)]);
        Assert.Equal(2u, FateRanker.PickBest(w, Cfg)!.Id);
    }

    [Fact] // A7
    public void A7_bonus_only_returns_null_when_no_bonus_exists()
    {
        var w = TestData.World(fates: [TestData.Fate(id: 1, x: 400, z: 0), TestData.Fate(id: 2, x: 300, z: 300)]);
        Assert.Null(FateRanker.PickBest(w, new SelectionConfig { BonusOnly = true }));
    }

    [Fact] // A9: inside or within 50y overrides the ladder
    public void A9_nearby_fate_taken_immediately()
    {
        var near = TestData.Fate(id: 1, x: 30, z: 0, progress: 0, radius: 10);
        var better = TestData.Fate(id: 2, x: 800, z: 800, progress: 70);
        var w = TestData.World(fates: [near, better]);
        Assert.Equal(1u, FateRanker.PickBest(w, Cfg)!.Id);
    }

    [Fact] // A10: during grace, distant fates are held off
    public void A10_grace_window_holds_distant_fates()
    {
        var far = TestData.Fate(id: 1, x: 800, z: 800);
        var w = TestData.World(now: 10_000, fates: [far]);
        Assert.Null(FateRanker.PickBest(w, Cfg, lastFateEndEpoch: 9_998));
        Assert.NotNull(FateRanker.PickBest(w, Cfg, lastFateEndEpoch: 9_000));
    }

    [Fact] // A11: full tie broken by lowest id
    public void A11_tie_breaks_by_lowest_id()
    {
        var w = TestData.World(fates: [TestData.Fate(id: 9, x: 400, z: 0), TestData.Fate(id: 3, x: 400, z: 0)]);
        Assert.Equal(3u, FateRanker.PickBest(w, Cfg)!.Id);
    }

    [Fact] // A12: teleport route can beat long direct flight
    public void A12_teleport_cost_model_prefers_aetheryte_route()
    {
        // Player at origin. Fate 1 direct 600 away. Fate 2 direct 900 away but 50 from an aetheryte: cost 50+200=250.
        var a = TestData.Fate(id: 1, x: 600, z: 0);
        var b = TestData.Fate(id: 2, x: 900, z: 0);
        var cfg = new SelectionConfig { Priority = [SelectionCriterion.DistanceTeleport] };
        var w = TestData.World(
            fates: [a, b],
            aetherytes: [new Aetheryte(10, new System.Numerics.Vector3(850, 0, 0))]);
        Assert.Equal(2u, FateRanker.PickBest(w, cfg)!.Id);
    }

    [Fact]
    public void Returns_null_when_nothing_eligible()
        => Assert.Null(FateRanker.PickBest(TestData.World(), Cfg));

    [Fact] // A9: standing over a ring on a rise still counts as inside it
    public void A9_inside_ring_on_a_rise_overrides_ladder()
    {
        var under = TestData.Fate(id: 1, x: 100, z: 100, progress: 0, radius: 30);
        var better = TestData.Fate(id: 2, x: 800, z: 800, progress: 70);
        var w = TestData.World(player: TestData.Player(x: 120, y: 60, z: 100), fates: [under, better]);
        Assert.Equal(1u, FateRanker.PickBest(w, Cfg)!.Id);
    }
}
