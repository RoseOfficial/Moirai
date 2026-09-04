using Moirai.Core.Planning;

namespace Moirai.Tests;

public class InterruptTests
{
    [Fact] // D4: each busy flag alone triggers the guard
    public void D4_busy_flags_trigger_guard()
    {
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(player: TestData.Player(casting: true))));
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(player: TestData.Player(betweenAreas: true))));
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(player: TestData.Player(jumping: true))));
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(player: TestData.Player(beingMoved: true))));
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(player: TestData.Player(occupied: true))));
        Assert.Equal(InterruptKind.Busy, Eval(TestData.World(lifestreamBusy: true)));
    }

    [Fact] // spec §3 precedence: busy outranks death (wait out transitions even while dead)
    public void Busy_outranks_death()
        => Assert.Equal(InterruptKind.Busy,
            Eval(TestData.World(player: TestData.Player(dead: true, betweenAreas: true))));

    [Fact] // D1 entry: death detected
    public void Death_detected()
        => Assert.Equal(InterruptKind.Dead, Eval(TestData.World(player: TestData.Player(dead: true))));

    [Fact] // D3: combat outside the current fate is unexpected
    public void D3_combat_outside_fate_is_unexpected()
        => Assert.Equal(InterruptKind.UnexpectedCombat,
            Eval(TestData.World(player: TestData.Player(inCombat: true))));

    [Fact] // D3: combat inside the current fate ring is expected
    public void Combat_inside_current_fate_is_not_an_interrupt()
    {
        var f = TestData.Fate(id: 5, x: 0, z: 0, radius: 60);
        var w = TestData.World(player: TestData.Player(inCombat: true), fates: [f]);
        Assert.Equal(InterruptKind.None, InterruptEvaluator.Evaluate(w, currentFateId: 5));
    }

    [Fact] // D5
    public void D5_navmesh_not_ready_holds()
        => Assert.Equal(InterruptKind.NavmeshNotReady, Eval(TestData.World(navmeshReady: false)));

    private static InterruptKind Eval(Moirai.Core.Model.WorldSnapshot w)
        => InterruptEvaluator.Evaluate(w, null);

    [Fact] // D3: the ring is horizontal, so combat on a rise inside it is the fate's own combat
    public void D3_combat_inside_ring_on_a_rise_is_not_unexpected()
    {
        var f = TestData.Fate(id: 5, x: 0, z: 0, radius: 40);
        var w = TestData.World(player: TestData.Player(x: 10, y: 45, z: 0, inCombat: true), fates: [f]);
        Assert.Equal(InterruptKind.None, InterruptEvaluator.Evaluate(w, currentFateId: 5));
    }
}
