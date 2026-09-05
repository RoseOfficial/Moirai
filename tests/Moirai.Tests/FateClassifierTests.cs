using Moirai.Core.Model;

namespace Moirai.Tests;

public class FateClassifierTests
{
    // The Fate sheet's Icon column: one map icon per objective kind
    private const uint BattleIcon = 60721, BossIcon = 60722, CollectIcon = 60723, DefendIcon = 60724, EscortIcon = 60725;

    private static FateKind Classify(uint eventItemId, uint rule, uint icon, FatePhase phase, long startTimeEpoch, int progress)
        => FateClassifier.Classify(eventItemId, rule, icon, phase, startTimeEpoch, progress);

    [Fact] // B11: an unopened collect fate must be started at its NPC before it can be collected
    public void B11_unopened_collect_fate_is_npc_start()
        => Assert.Equal(FateKind.NpcStart, Classify(900, 2, CollectIcon, FatePhase.Preparing, 0, 0));

    [Fact] // B11: the game may report a start time while the fate is still preparing — the phase wins
    public void B11_preparing_collect_fate_is_npc_start_even_with_a_start_time()
        => Assert.Equal(FateKind.NpcStart, Classify(900, 2, CollectIcon, FatePhase.Preparing, 5_000, 0));

    [Fact] // B11: once running, the same fate is a collect fate
    public void B11_running_collect_fate_is_collect()
        => Assert.Equal(FateKind.Collect, Classify(900, 2, CollectIcon, FatePhase.Running, 5_000, 0));

    [Fact] // a preparing fate is npc-start whatever its sheet rule says
    public void Preparing_fate_is_npc_start_regardless_of_rule()
        => Assert.Equal(FateKind.NpcStart, Classify(0, 4, DefendIcon, FatePhase.Preparing, 5_000, 0));

    [Fact] // a fate with no start time and no progress is unopened even if its phase already reads running
    public void No_start_time_and_no_progress_is_npc_start()
        => Assert.Equal(FateKind.NpcStart, Classify(0, 1, BattleIcon, FatePhase.Running, 0, 0));

    [Theory] // B12: the Rule column names collect, escort and defend; among plain kill fates the boss icon marks a boss
    [InlineData(1u, BattleIcon, FateKind.Battle)]
    [InlineData(1u, BossIcon, FateKind.Boss)]
    [InlineData(2u, CollectIcon, FateKind.Collect)]
    [InlineData(3u, EscortIcon, FateKind.Escort)]
    [InlineData(4u, DefendIcon, FateKind.Defend)]
    [InlineData(4u, BossIcon, FateKind.Defend)]   // a defend fate keeps its rule even with a boss icon
    [InlineData(5u, BattleIcon, FateKind.Battle)] // special-content rules fall back to the icon
    [InlineData(6u, BossIcon, FateKind.Boss)]
    [InlineData(0u, 0u, FateKind.Battle)]
    public void B12_running_fate_kind_follows_sheet_rule_then_icon(uint rule, uint icon, FateKind expected)
        => Assert.Equal(expected, Classify(0, rule, icon, FatePhase.Running, 5_000, 10));

    [Fact] // B3/B12: a collect fate is collect by its rule before its event item populates
    public void B12_collect_rule_without_an_event_item_is_collect()
        => Assert.Equal(FateKind.Collect, Classify(0, 2, CollectIcon, FatePhase.Running, 5_000, 0));

    [Fact] // an event item is the strongest collect signal and beats the rule column
    public void Event_item_marks_collect_over_rule()
        => Assert.Equal(FateKind.Collect, Classify(900, 4, DefendIcon, FatePhase.Running, 5_000, 0));

    [Fact] // a running fate that already has progress is open even if its start time is missing
    public void Progress_without_start_time_counts_as_open()
        => Assert.Equal(FateKind.Battle, Classify(0, 1, BattleIcon, FatePhase.Running, 0, 5));

    // The Fate sheet's ScreenImageAccept column: the banner an ordinary fate opens with, and the big-boss one
    private const uint PlainBanner = 33, BigBossBanner = 37;

    [Theory] // B14: a boss that opens with the big-boss banner is a special boss; the banner alone makes nothing a boss
    [InlineData(FateKind.Boss, BigBossBanner, true)]
    [InlineData(FateKind.Boss, PlainBanner, false)]
    [InlineData(FateKind.Boss, 0u, false)]           // sheet row unavailable
    [InlineData(FateKind.Battle, BigBossBanner, false)] // a chain's battle step opens with the banner too
    [InlineData(FateKind.Defend, BigBossBanner, false)]
    public void B14_special_boss_is_a_boss_with_the_big_boss_banner(FateKind sheetKind, uint banner, bool expected)
        => Assert.Equal(expected, FateClassifier.IsSpecialBoss(sheetKind, banner));
}
