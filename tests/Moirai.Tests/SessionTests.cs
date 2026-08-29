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

    [Fact] // B8: adopt a new fate at the same site
    public void B8_adopts_continuation_at_same_site()
    {
        var done = TestData.Fate(id: 1, x: 100, z: 100, continuation: true);
        var watcher = new ContinuationWatcher();
        watcher.Arm(done, nowEpoch: 1_000);

        var (waiting, _) = watcher.Tick(TestData.World(now: 1_005));
        Assert.Equal(ContinuationState.Waiting, waiting);

        var chain = TestData.Fate(id: 2, x: 110, z: 100);
        var (adopted, fate) = watcher.Tick(TestData.World(now: 1_010, fates: [chain]));
        Assert.Equal(ContinuationState.Adopted, adopted);
        Assert.Equal(2u, fate!.Id);
    }

    [Fact] // B8: give up after 30 s
    public void B8_gives_up_after_timeout()
    {
        var watcher = new ContinuationWatcher(timeoutSeconds: 30);
        watcher.Arm(TestData.Fate(id: 1), nowEpoch: 1_000);
        var (state, _) = watcher.Tick(TestData.World(now: 1_031));
        Assert.Equal(ContinuationState.GaveUp, state);
        Assert.Equal(ContinuationState.Idle, watcher.Tick(TestData.World(now: 1_032)).Item1);
    }

    [Fact] // B8: the old fate id itself is never adopted
    public void B8_never_adopts_the_old_fate()
    {
        var done = TestData.Fate(id: 1, x: 100, z: 100);
        var watcher = new ContinuationWatcher();
        watcher.Arm(done, nowEpoch: 1_000);
        var (state, _) = watcher.Tick(TestData.World(now: 1_005, fates: [done]));
        Assert.Equal(ContinuationState.Waiting, state);
    }
}
