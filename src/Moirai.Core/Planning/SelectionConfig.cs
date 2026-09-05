namespace Moirai.Core.Planning;

public enum SelectionCriterion { Progress, Bonus, TimeLeft, Distance, DistanceTeleport }

public sealed class SelectionConfig
{
    public int MinTimeLeftSeconds { get; init; } = 180;       // A1
    public int MaxProgressPercent { get; init; } = 80;        // A2
    public int LevelMargin { get; init; } = 2;                // A4
    public int BossJoinProgress { get; init; }                // A5
    public int SpecialBossJoinProgress { get; init; } = 20;   // A5
    public float NearbyOverrideDistance { get; init; } = 50f; // A9
    public float TeleportPenalty { get; init; } = 200f;       // A12
    public int PostFateGraceSeconds { get; init; } = 5;       // A10
    public bool BonusOnly { get; init; }                      // A7
    public bool SkipCollectFates { get; init; }               // A14: collect fates left alone, open or not
    public bool SkipNpcStartFates { get; init; }              // A15: kill fates waiting at a starter NPC left alone
    public IReadOnlyList<SelectionCriterion> Priority { get; init; } =
        [SelectionCriterion.Progress, SelectionCriterion.Bonus, SelectionCriterion.TimeLeft, SelectionCriterion.DistanceTeleport];
    public IReadOnlySet<uint> Blacklist { get; init; } = new HashSet<uint>();
}
