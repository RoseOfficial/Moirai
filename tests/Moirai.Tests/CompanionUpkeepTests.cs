using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class CompanionUpkeepTests
{
    private const uint Greens = 4868;
    private const uint Healer = 7;
    private const uint Defender = 5;

    private static CompanionConfig Cfg(bool enabled = true, uint stance = Healer, int below = 300)
        => new() { Enabled = enabled, GreensItemId = Greens, StanceActionId = stance, ResummonBelowSeconds = below };

    private static WorldSnapshot W(
        bool summoned = false, int timeLeft = 0, uint stance = 0, int greens = 5,
        bool mounted = false, bool inCombat = false, long now = 10_000, ushort territory = 0)
        => TestData.World(
            now: now, territory: territory,
            player: TestData.Player(mounted: mounted, inCombat: inCombat,
                companionSummoned: summoned, companionTimeLeft: timeLeft, companionStance: stance),
            items: new Dictionary<uint, int> { [Greens] = greens });

    [Fact] // F1: no companion out -> use greens
    public void F1_summons_when_companion_missing()
    {
        var act = Assert.IsType<SummonCompanion>(new CompanionUpkeep(Cfg()).Tick(W()));
        Assert.Equal(Greens, act.GreensItemId);
    }

    [Fact] // F2: healthy companion in the right stance -> nothing to do
    public void F2_leaves_a_healthy_companion_alone()
        => Assert.Null(new CompanionUpkeep(Cfg()).Tick(W(summoned: true, timeLeft: 1200, stance: Healer)));

    [Fact] // F3: timer under the threshold -> greens again to extend
    public void F3_extends_when_timer_below_threshold()
        => Assert.IsType<SummonCompanion>(
            new CompanionUpkeep(Cfg(below: 300)).Tick(W(summoned: true, timeLeft: 120, stance: Healer)));

    [Theory] // F4: never waste a green while mounted, never interrupt a fight
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void F4_holds_while_mounted_or_in_combat(bool mounted, bool inCombat)
        => Assert.Null(new CompanionUpkeep(Cfg()).Tick(W(mounted: mounted, inCombat: inCombat)));

    [Fact] // F5: no greens -> nothing issued, reason surfaced
    public void F5_without_greens_does_nothing_and_reports()
    {
        var up = new CompanionUpkeep(Cfg());
        var w = W(greens: 0);
        Assert.Null(up.Tick(w));
        Assert.True(up.OutOfGreens(w));
        Assert.Contains("Greens", up.Note);
    }

    [Fact] // F5: an already-summoned companion is not "out of greens" yet
    public void F5_summoned_companion_is_not_out_of_greens()
        => Assert.False(new CompanionUpkeep(Cfg()).OutOfGreens(W(summoned: true, timeLeft: 600, greens: 0)));

    [Fact] // F6: stance differs from the configured one -> set it
    public void F6_sets_stance_when_it_differs()
    {
        var act = Assert.IsType<SetCompanionStance>(
            new CompanionUpkeep(Cfg(stance: Healer)).Tick(W(summoned: true, timeLeft: 1200, stance: Defender)));
        Assert.Equal(Healer, act.StanceActionId);
    }

    [Fact] // F6: stance 0 means leave whatever the player chose
    public void F6_no_stance_config_never_changes_stance()
        => Assert.Null(new CompanionUpkeep(Cfg(stance: 0)).Tick(W(summoned: true, timeLeft: 1200, stance: Defender)));

    [Fact] // F6: stance is set before the timer is topped up
    public void F6_stance_comes_before_extension()
        => Assert.IsType<SetCompanionStance>(
            new CompanionUpkeep(Cfg(stance: Healer, below: 300)).Tick(W(summoned: true, timeLeft: 60, stance: Defender)));

    [Fact] // F7: one greens use per cooldown window
    public void F7_waits_out_the_summon_cooldown()
    {
        var up = new CompanionUpkeep(Cfg());
        Assert.IsType<SummonCompanion>(up.Tick(W(now: 1_000)));
        Assert.Null(up.Tick(W(now: 1_002)));
        Assert.IsType<SummonCompanion>(up.Tick(W(now: 1_000 + CompanionUpkeep.SummonCooldownSeconds)));
    }

    [Fact] // F8: bounded retries; a zone change earns a fresh set
    public void F8_gives_up_after_repeated_failed_summons_until_zone_change()
    {
        var up = new CompanionUpkeep(Cfg());
        var now = 1_000L;
        for (var i = 0; i < CompanionUpkeep.MaxSummonAttempts; i++, now += CompanionUpkeep.SummonCooldownSeconds)
            Assert.IsType<SummonCompanion>(up.Tick(W(now: now)));
        Assert.Null(up.Tick(W(now: now)));
        Assert.NotNull(up.Note);
        Assert.IsType<SummonCompanion>(up.Tick(W(now: now + 60, territory: 5)));
    }

    [Fact] // F8: a companion that shows up resets the attempt budget
    public void F8_success_resets_attempts()
    {
        var up = new CompanionUpkeep(Cfg(stance: 0));
        var now = 1_000L;
        for (var i = 0; i < CompanionUpkeep.MaxSummonAttempts - 1; i++, now += CompanionUpkeep.SummonCooldownSeconds)
            Assert.IsType<SummonCompanion>(up.Tick(W(now: now)));
        Assert.Null(up.Tick(W(summoned: true, timeLeft: 1800, now: now)));       // it appeared
        now += 1800;
        for (var i = 0; i < CompanionUpkeep.MaxSummonAttempts; i++, now += CompanionUpkeep.SummonCooldownSeconds)
            Assert.IsType<SummonCompanion>(up.Tick(W(now: now)));                 // full budget again
    }

    [Fact] // disabled -> inert
    public void Disabled_never_acts()
    {
        var up = new CompanionUpkeep(Cfg(enabled: false));
        Assert.Null(up.Tick(W()));
        Assert.False(up.OutOfGreens(W(greens: 0)));
    }
}
