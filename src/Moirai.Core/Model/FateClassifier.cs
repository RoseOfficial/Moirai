namespace Moirai.Core.Model;

public static class FateClassifier
{
    // Sheet-only kind: an event item is the strongest collect signal; otherwise the Rule column decides
    public static FateKind FromSheet(uint eventItemId, uint rule)
        => eventItemId != 0
            ? FateKind.Collect
            : rule switch { 4 => FateKind.Boss, 5 => FateKind.Defend, 6 => FateKind.Escort, _ => FateKind.Battle };

    // A8/B11: a fate that nobody has opened yet. The phase is the game's own word for it; the
    // start-time field can already be set while a fate still waits at its NPC, so it only
    // counts as a second signal when it is missing altogether.
    public static bool IsUnopened(FatePhase phase, long startTimeEpoch, int progress)
        => phase == FatePhase.Preparing || (startTimeEpoch == 0 && progress == 0);

    // B11: an unopened fate must be started at its NPC whatever its sheet kind.
    // Collect fates included — they open at the same NPC they later hand in to.
    public static FateKind Classify(uint eventItemId, uint rule, FatePhase phase, long startTimeEpoch, int progress)
        => IsUnopened(phase, startTimeEpoch, progress)
            ? FateKind.NpcStart
            : FromSheet(eventItemId, rule);
}
