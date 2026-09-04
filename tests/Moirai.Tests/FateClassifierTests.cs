using Moirai.Core.Model;

namespace Moirai.Tests;

public class FateClassifierTests
{
    [Fact] // B11: an unopened collect fate must be started at its NPC before it can be collected
    public void B11_unopened_collect_fate_is_npc_start()
        => Assert.Equal(FateKind.NpcStart, FateClassifier.Classify(eventItemId: 900, rule: 2, startTimeEpoch: 0, progress: 0));

    [Fact] // B11: once opened, the same fate is a collect fate
    public void B11_opened_collect_fate_is_collect()
        => Assert.Equal(FateKind.Collect, FateClassifier.Classify(eventItemId: 900, rule: 2, startTimeEpoch: 5_000, progress: 0));

    [Fact] // an unopened fate is npc-start whatever its sheet rule says
    public void Unopened_fate_is_npc_start_regardless_of_rule()
        => Assert.Equal(FateKind.NpcStart, FateClassifier.Classify(eventItemId: 0, rule: 4, startTimeEpoch: 0, progress: 0));

    [Theory] // a running fate's kind follows the sheet rule
    [InlineData(1u, FateKind.Battle)]
    [InlineData(4u, FateKind.Boss)]
    [InlineData(5u, FateKind.Defend)]
    [InlineData(6u, FateKind.Escort)]
    [InlineData(0u, FateKind.Battle)]
    public void Running_fate_kind_follows_sheet_rule(uint rule, FateKind expected)
        => Assert.Equal(expected, FateClassifier.Classify(eventItemId: 0, rule: rule, startTimeEpoch: 5_000, progress: 10));

    [Fact] // an event item is the strongest collect signal and beats the rule column
    public void Event_item_marks_collect_over_rule()
        => Assert.Equal(FateKind.Collect, FateClassifier.Classify(eventItemId: 900, rule: 4, startTimeEpoch: 5_000, progress: 0));

    [Fact] // a fate that already has progress is open even if its start time is missing
    public void Progress_without_start_time_counts_as_open()
        => Assert.Equal(FateKind.Battle, FateClassifier.Classify(eventItemId: 0, rule: 1, startTimeEpoch: 0, progress: 5));
}
