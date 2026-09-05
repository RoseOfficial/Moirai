using Moirai.Core.Model;
using Moirai.Core.Planning;

namespace Moirai.Tests;

public class SelectionGatesTests
{
    private static readonly SelectionConfig Cfg = new();

    private static SkipReason Eval(FateSnapshot f, SelectionConfig? c = null)
        => SelectionGates.Evaluate(f, TestData.World(fates: [f]), c ?? Cfg);

    [Fact] // A1
    public void A1_skips_fates_below_min_time_left()
        => Assert.Equal(SkipReason.TooLittleTime, Eval(TestData.Fate(timeRemaining: 179)));

    [Fact] // A2
    public void A2_skips_fates_above_max_progress()
        => Assert.Equal(SkipReason.TooFarAlong, Eval(TestData.Fate(progress: 81)));

    [Fact] // A3
    public void A3_skips_unregistered_zero_coordinate_fates()
        => Assert.Equal(SkipReason.NotRegistered, Eval(TestData.Fate(x: 0, z: 0)));

    [Fact] // A4
    public void A4_skips_fates_above_player_level_plus_margin()
    {
        var f = TestData.Fate(maxLevel: 53);
        var w = TestData.World(player: TestData.Player(level: 50), fates: [f]);
        Assert.Equal(SkipReason.AboveLevel, SelectionGates.Evaluate(f, w, Cfg));
    }

    [Fact] // A4: no lower-level floor
    public void A4_never_skips_low_level_fates()
        => Assert.Equal(SkipReason.None, Eval(TestData.Fate(maxLevel: 5)));

    [Fact] // A5
    public void A5_boss_fate_ineligible_below_join_threshold()
    {
        var cfg = new SelectionConfig { BossJoinProgress = 10 };
        Assert.Equal(SkipReason.BossTooEarly, Eval(TestData.Fate(kind: FateKind.Boss, progress: 5), cfg));
        Assert.Equal(SkipReason.None, Eval(TestData.Fate(kind: FateKind.Boss, progress: 10), cfg));
    }

    [Fact] // A5: special bosses use their own threshold
    public void A5_special_boss_uses_special_threshold()
        => Assert.Equal(SkipReason.BossTooEarly,
            Eval(TestData.Fate(kind: FateKind.Boss, specialBoss: true, progress: 19)));

    [Fact] // A7 gate half: bonus-only rejects non-bonus
    public void A7_bonus_only_rejects_non_bonus()
        => Assert.Equal(SkipReason.NotBonus, Eval(TestData.Fate(), new SelectionConfig { BonusOnly = true }));

    [Fact]
    public void Blacklisted_fate_is_skipped()
        => Assert.Equal(SkipReason.Blacklisted,
            Eval(TestData.Fate(id: 77), new SelectionConfig { Blacklist = new HashSet<uint> { 77 } }));

    [Fact]
    public void Ended_fate_is_skipped()
        => Assert.Equal(SkipReason.WrongPhase, Eval(TestData.Fate(phase: FatePhase.Ended)));

    [Fact] // D9: a fate the player died in is skipped for the session
    public void D9_fate_that_killed_us_is_skipped()
    {
        var f = TestData.Fate(id: 218);
        var w = TestData.World(fates: [f]);
        Assert.Equal(SkipReason.KilledUs, SelectionGates.Evaluate(f, w, Cfg, deadly: new HashSet<uint> { 218 }));
        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(f, w, Cfg, deadly: new HashSet<uint> { 150 }));
    }
}
