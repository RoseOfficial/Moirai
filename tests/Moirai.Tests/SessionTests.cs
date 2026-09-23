using Moirai.Core.Model;
using Moirai.Core.Session;

namespace Moirai.Tests;

public class SessionTests
{
    [Fact] // D2: failed is never completed
    public void D2_failed_fate_is_not_a_completion()
    {
        Assert.Equal(FateOutcome.Completed, SessionLedger.OutcomeFrom(FatePhase.Ended));
        Assert.Equal(FateOutcome.Failed, SessionLedger.OutcomeFrom(FatePhase.Failed));
        Assert.Equal(FateOutcome.Abandoned, SessionLedger.OutcomeFrom(FatePhase.Running));

        var ledger = new SessionLedger();
        ledger.Record(FateOutcome.Failed);
        Assert.Equal(0, ledger.Completed);
        Assert.Equal(1, ledger.Failed);
    }

    [Fact] // D6: latch pends until the fate leaves the table
    public void D6_reward_latch_clears_when_fate_leaves_table()
    {
        var latch = new RewardLatch();
        latch.Arm(7);
        Assert.True(latch.IsPending);

        latch.Observe(TestData.World(fates: [TestData.Fate(id: 7, phase: FatePhase.Ended)]));
        Assert.True(latch.IsPending); // still in the table -> payout not registered

        latch.Observe(TestData.World());
        Assert.False(latch.IsPending);
    }

    [Fact] // overlay: elapsed time and completions per hour follow the observed clock
    public void Ledger_tracks_elapsed_time_and_completion_rate()
    {
        var ledger = new SessionLedger();
        Assert.Equal(0, ledger.ElapsedSeconds);
        Assert.Equal(0, ledger.CompletedPerHour);

        ledger.Observe(1_000);
        ledger.Record(FateOutcome.Completed);
        ledger.Record(FateOutcome.Completed);
        ledger.Observe(1_000 + 1_800);

        Assert.Equal(1_800, ledger.ElapsedSeconds);
        Assert.Equal(4.0, ledger.CompletedPerHour, 3);
    }

    [Fact] // §11 Paused: time spent paused is not session time, so the rate is not diluted by it
    public void Ledger_does_not_count_a_pause()
    {
        var ledger = new SessionLedger();
        ledger.Observe(1_000);
        ledger.Observe(1_600);
        ledger.Pause();
        ledger.Observe(5_000); // the first tick after the resume starts the clock again
        ledger.Observe(5_060);

        Assert.Equal(660, ledger.ElapsedSeconds);
    }
}
