using Moirai.Core.Model;

namespace Moirai.Tests;

public class FateClassifierTests
{
    private static FateKind Classify(uint eventItemId, uint rule, FatePhase phase, long startTimeEpoch, int progress)
        => FateClassifier.Classify(eventItemId, rule, phase, startTimeEpoch, progress);

    [Fact] // B11: an unopened collect fate must be started at its NPC before it can be collected
    public void B11_unopened_collect_fate_is_npc_start()
        => Assert.Equal(FateKind.NpcStart, Classify(900, 2, FatePhase.Preparing, 0, 0));

    [Fact] // B11: the game may report a start time while the fate is still preparing — the phase wins
    public void B11_preparing_collect_fate_is_npc_start_even_with_a_start_time()
        => Assert.Equal(FateKind.NpcStart, Classify(900, 2, FatePhase.Preparing, 5_000, 0));

    [Fact] // B11: once running, the same fate is a collect fate
    public void B11_running_collect_fate_is_collect()
        => Assert.Equal(FateKind.Collect, Classify(900, 2, FatePhase.Running, 5_000, 0));

    [Fact] // a preparing fate is npc-start whatever its sheet rule says
    public void Preparing_fate_is_npc_start_regardless_of_rule()
        => Assert.Equal(FateKind.NpcStart, Classify(0, 4, FatePhase.Preparing, 5_000, 0));

    [Fact] // a fate with no start time and no progress is unopened even if its phase already reads running
    public void No_start_time_and_no_progress_is_npc_start()
        => Assert.Equal(FateKind.NpcStart, Classify(0, 1, FatePhase.Running, 0, 0));

    [Theory] // a running fate's kind follows the sheet rule
    [InlineData(1u, FateKind.Battle)]
    [InlineData(4u, FateKind.Boss)]
    [InlineData(5u, FateKind.Defend)]
    [InlineData(6u, FateKind.Escort)]
    [InlineData(0u, FateKind.Battle)]
    public void Running_fate_kind_follows_sheet_rule(uint rule, FateKind expected)
        => Assert.Equal(expected, Classify(0, rule, FatePhase.Running, 5_000, 10));

    [Fact] // an event item is the strongest collect signal and beats the rule column
    public void Event_item_marks_collect_over_rule()
        => Assert.Equal(FateKind.Collect, Classify(900, 4, FatePhase.Running, 5_000, 0));

    [Fact] // a running fate that already has progress is open even if its start time is missing
    public void Progress_without_start_time_counts_as_open()
        => Assert.Equal(FateKind.Battle, Classify(0, 1, FatePhase.Running, 0, 5));
}
