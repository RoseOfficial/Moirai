using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class GearUpkeepTests
{
    private static GearConfig Cfg(bool enabled = true, int below = 30, bool stopWhenBroken = false)
        => new() { Enabled = enabled, RepairBelowPercent = below, StopWhenBroken = stopWhenBroken };

    private static WorldSnapshot W(
        GearPiece[]? gear = null, bool mounted = false, bool inCombat = false, bool casting = false,
        bool repairReady = true, long now = 10_000)
        => TestData.World(now: now, repairReady: repairReady,
            player: TestData.Player(mounted: mounted, inCombat: inCombat, casting: casting,
                gear: gear ?? [new GearPiece(90, true), new GearPiece(80, true)]));

    private static readonly GearPiece[] Worn = [new GearPiece(90, true), new GearPiece(12, true)];

    [Fact] // R1: a piece below the threshold that self-repair can mend is repaired
    public void R1_repairs_a_mendable_piece_below_the_threshold()
        => Assert.IsType<RepairGear>(new GearUpkeep(Cfg()).Tick(W(Worn)));

    [Fact] // R1: gear at or above the threshold is left alone
    public void R1_leaves_gear_above_the_threshold_alone()
    {
        var up = new GearUpkeep(Cfg(below: 30));
        Assert.Null(up.Tick(W([new GearPiece(30, true), new GearPiece(100, true)])));
        Assert.Null(up.Note);
    }

    [Fact] // R2: a piece self-repair cannot mend (crafter level, Dark Matter grade) is not asked about, and the overlay says so
    public void R2_a_piece_that_cannot_be_self_repaired_is_noted()
    {
        var up = new GearUpkeep(Cfg());
        Assert.Null(up.Tick(W([new GearPiece(90, true), new GearPiece(12, false)])));
        Assert.Contains("cannot be self-repaired", up.Note);
    }

    [Theory] // R3: never from the saddle, in a fight, or over a cast
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void R3_holds_while_mounted_in_combat_or_casting(bool mounted, bool inCombat, bool casting)
        => Assert.Null(new GearUpkeep(Cfg()).Tick(W(Worn, mounted, inCombat, casting)));

    [Fact] // R4: a repair that does not bring the gear back up is tried again once after a wait, then given up for the session
    public void R4_gives_up_after_two_repairs_that_did_not_take()
    {
        var up = new GearUpkeep(Cfg());
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 10_000)));
        Assert.Null(up.Tick(W(Worn, now: 10_001)));                  // the retry waits
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 10_000 + GearUpkeep.RetrySeconds)));
        Assert.Null(up.Tick(W(Worn, now: 10_000 + 3 * GearUpkeep.RetrySeconds)));
        Assert.Contains("did not come back up", up.Note);
    }

    [Fact] // R4: a repair that worked restores the budget for the next time the gear wears down
    public void R4_a_repair_that_worked_restores_the_budget()
    {
        var up = new GearUpkeep(Cfg());
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 10_000)));
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 10_000 + GearUpkeep.RetrySeconds)));
        Assert.Null(up.Tick(W([new GearPiece(100, true)], now: 10_100))); // mended
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 50_000)));         // worn down again hours later
    }

    [Fact] // R4: given up, then mended by other means (by hand, at a mender): the next wear-down is tried afresh
    public void R4_gear_mended_after_giving_up_is_tried_again_later()
    {
        var up = new GearUpkeep(Cfg());
        up.Tick(W(Worn, now: 10_000));
        up.Tick(W(Worn, now: 10_000 + GearUpkeep.RetrySeconds));
        Assert.Null(up.Tick(W(Worn, now: 10_000 + 3 * GearUpkeep.RetrySeconds))); // given up
        Assert.Null(up.Tick(W([new GearPiece(100, true)], now: 20_000)));          // mended by hand
        Assert.IsType<RepairGear>(up.Tick(W(Worn, now: 60_000)));
    }

    [Fact] // R5: once the shell could not work the repair window, nothing more is asked this run
    public void R5_a_failed_repair_window_stops_the_asks()
    {
        var up = new GearUpkeep(Cfg());
        Assert.Null(up.Tick(W(Worn, repairReady: false)));
        Assert.NotNull(up.Note);
    }

    [Fact] // R6: switched off, nothing is repaired
    public void R6_disabled_repairs_nothing()
        => Assert.Null(new GearUpkeep(Cfg(enabled: false)).Tick(W(Worn)));

    [Fact] // R7: a broken piece with a repair coming is not a reason to stop
    public void R7_broken_but_mendable_is_not_broken_for_good()
        => Assert.False(new GearUpkeep(Cfg(stopWhenBroken: true)).Broken(W([new GearPiece(0, true)])));

    [Fact] // R7: a broken piece nothing will mend is
    public void R7_broken_and_not_mendable_is_broken_for_good()
    {
        Assert.True(new GearUpkeep(Cfg(stopWhenBroken: true)).Broken(W([new GearPiece(0, false)])));
        Assert.True(new GearUpkeep(Cfg(enabled: false, stopWhenBroken: true)).Broken(W([new GearPiece(0, true)])));
        Assert.True(new GearUpkeep(Cfg(stopWhenBroken: true)).Broken(W([new GearPiece(0, true)], repairReady: false)));
    }

    [Fact] // R7: worn but not broken is not broken
    public void R7_worn_gear_is_not_broken()
        => Assert.False(new GearUpkeep(Cfg(stopWhenBroken: true)).Broken(W([new GearPiece(1, false)])));
}
