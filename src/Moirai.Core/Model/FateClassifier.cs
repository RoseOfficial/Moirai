namespace Moirai.Core.Model;

public static class FateClassifier
{
    // The Fate sheet's Icon column for a boss fate; the other kinds each have a Rule value of their own
    public const uint BossIcon = 60722;

    // B12: sheet-only kind. An event item is the strongest collect signal; otherwise the Rule column
    // names collect (2), escort (3) and defend (4), and the plain kill rule (1) is split into battle
    // and boss by the icon alone. Higher rules are special content and fall back to the icon too.
    public static FateKind FromSheet(uint eventItemId, uint rule, uint icon)
        => eventItemId != 0
            ? FateKind.Collect
            : rule switch
            {
                2 => FateKind.Collect,
                3 => FateKind.Escort,
                4 => FateKind.Defend,
                _ => icon == BossIcon ? FateKind.Boss : FateKind.Battle,
            };

    // A8/B11: a fate that nobody has opened yet. The phase is the game's own word for it; the
    // start-time field can already be set while a fate still waits at its NPC, so it only
    // counts as a second signal when it is missing altogether.
    public static bool IsUnopened(FatePhase phase, long startTimeEpoch, int progress)
        => phase == FatePhase.Preparing || (startTimeEpoch == 0 && progress == 0);

    // B11: an unopened fate must be started at its NPC whatever its sheet kind.
    // Collect fates included — they open at the same NPC they later hand in to.
    public static FateKind Classify(uint eventItemId, uint rule, uint icon, FatePhase phase, long startTimeEpoch, int progress)
        => IsUnopened(phase, startTimeEpoch, progress)
            ? FateKind.NpcStart
            : FromSheet(eventItemId, rule, icon);
}
