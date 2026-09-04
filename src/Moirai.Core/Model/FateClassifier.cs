namespace Moirai.Core.Model;

public static class FateClassifier
{
    // Sheet-only kind: an event item is the strongest collect signal; otherwise the Rule column decides
    public static FateKind FromSheet(uint eventItemId, uint rule)
        => eventItemId != 0
            ? FateKind.Collect
            : rule switch { 4 => FateKind.Boss, 5 => FateKind.Defend, 6 => FateKind.Escort, _ => FateKind.Battle };

    // B11: an unopened fate (no start time, no progress) must be started at its NPC whatever its
    // sheet kind. Collect fates included — they open at the same NPC they later hand in to.
    public static FateKind Classify(uint eventItemId, uint rule, long startTimeEpoch, int progress)
        => startTimeEpoch == 0 && progress == 0
            ? FateKind.NpcStart
            : FromSheet(eventItemId, rule);
}
