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
        var skips = new SessionSkipList();
        skips.Add(218, SkipReason.KilledUs);
        Assert.Equal(SkipReason.KilledUs, SelectionGates.Evaluate(f, w, Cfg, skips));
        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(TestData.Fate(id: 150), w, Cfg, skips));
    }

    [Fact] // A14: the collect switch covers collect fates open or not, by their sheet kind, and nothing else
    public void A14_skip_collect_covers_open_and_unopened_collect_fates()
    {
        var c = new SelectionConfig { SkipCollectFates = true };
        var w = TestData.World();
        var open = TestData.Fate(id: 601, kind: FateKind.Collect, eventItemId: 2001053);
        var unopened = TestData.Fate(id: 601, kind: FateKind.NpcStart, phase: FatePhase.Preparing, startTimeEpoch: 0, progress: 0, eventItemId: 2001053);
        var byRule = TestData.Fate(id: 7, kind: FateKind.Collect, eventItemId: 0);
        var battle = TestData.Fate(id: 8, kind: FateKind.Battle);

        Assert.Equal(SkipReason.CollectFate, SelectionGates.Evaluate(open, w, c));
        Assert.Equal(SkipReason.CollectFate, SelectionGates.Evaluate(unopened, w, c));
        Assert.Equal(SkipReason.CollectFate, SelectionGates.Evaluate(byRule, w, c));
        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(battle, w, c));
        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(open, w, new SelectionConfig()));
    }

    [Fact] // A15: the NPC-start switch covers kill fates waiting at a starter; collect fates follow the collect switch
    public void A15_skip_npc_start_exempts_collect_fates()
    {
        var w = TestData.World();
        var killAtNpc = TestData.Fate(id: 1, kind: FateKind.NpcStart, phase: FatePhase.Preparing, startTimeEpoch: 0, progress: 0);
        var collectAtNpc = TestData.Fate(id: 601, kind: FateKind.NpcStart, phase: FatePhase.Preparing, startTimeEpoch: 0, progress: 0, eventItemId: 2001053);

        var skipNpc = new SelectionConfig { SkipNpcStartFates = true };
        Assert.Equal(SkipReason.NpcStart, SelectionGates.Evaluate(killAtNpc, w, skipNpc));
        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(collectAtNpc, w, skipNpc)); // opened at its NPC, then collected

        var skipBoth = new SelectionConfig { SkipNpcStartFates = true, SkipCollectFates = true };
        Assert.Equal(SkipReason.CollectFate, SelectionGates.Evaluate(collectAtNpc, w, skipBoth));

        Assert.Equal(SkipReason.None, SelectionGates.Evaluate(killAtNpc, w, new SelectionConfig()));
    }

    [Fact] // D10: a fate the ladder gave up on is skipped for the session with its own reason
    public void D10_unreachable_fate_is_skipped()
    {
        var f = TestData.Fate(id: 218);
        var skips = new SessionSkipList();
        skips.Add(218, SkipReason.Unreachable);
        Assert.Equal(SkipReason.Unreachable, SelectionGates.Evaluate(f, TestData.World(fates: [f]), Cfg, skips));
    }
}
