using System.Numerics;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class RecoveryTests
{
    [Fact] // D7 + spec §7.2: bounded, escalating, terminal
    public void D7_ladder_escalates_and_terminates()
    {
        var ladder = new RecoveryLadder(perRungCap: 2);
        Assert.Equal(RecoveryRung.RePath, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.RePath, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.RerollDestination, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.RerollDestination, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.Escape, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.Escape, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.ReturnToAetheryte, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.ReturnToAetheryte, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.Exhausted, ladder.NextAttempt());
        Assert.Equal(RecoveryRung.Exhausted, ladder.NextAttempt()); // stays terminal
    }

    [Fact]
    public void Ladder_reset_starts_over()
    {
        var ladder = new RecoveryLadder(perRungCap: 1);
        ladder.NextAttempt();
        ladder.NextAttempt();
        ladder.Reset();
        Assert.Equal(RecoveryRung.RePath, ladder.NextAttempt());
    }

    [Fact] // C7/C8 sampler: stationary past window -> stuck
    public void Stationary_past_window_reports_stuck()
    {
        var d = new StuckDetector(minMove: 2f, windowMs: 2000);
        Assert.False(d.Sample(new Vector3(0, 0, 0), 0, suppress: false));
        Assert.False(d.Sample(new Vector3(0.5f, 0, 0), 1000, suppress: false));
        Assert.True(d.Sample(new Vector3(1.0f, 0, 0), 2100, suppress: false));
    }

    [Fact]
    public void Movement_resets_the_window()
    {
        var d = new StuckDetector(minMove: 2f, windowMs: 2000);
        d.Sample(new Vector3(0, 0, 0), 0, suppress: false);
        Assert.False(d.Sample(new Vector3(5, 0, 0), 1900, suppress: false));
        Assert.False(d.Sample(new Vector3(5, 0, 0), 3800, suppress: false));
        Assert.True(d.Sample(new Vector3(5, 0, 0), 4000, suppress: false));
    }

    [Fact] // C9: suppression re-anchors (no false positive during descent)
    public void C9_suppression_prevents_false_positive()
    {
        var d = new StuckDetector(minMove: 2f, windowMs: 2000);
        d.Sample(new Vector3(0, 0, 0), 0, suppress: false);
        Assert.False(d.Sample(new Vector3(0, 0, 0), 2100, suppress: true));
        Assert.False(d.Sample(new Vector3(0, 0, 0), 2200, suppress: false)); // re-anchored
    }
}
