using Dalamud.Configuration;
using Moirai.Core.Planning;

namespace Moirai;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // Session
    public int DeathCap { get; set; } = 3;
    public bool SkipNpcStartFates { get; set; } = true;
    public bool SkipCollectFates { get; set; } // A14: collect fates left alone, open or not
    public bool KeepRecording { get; set; } = true; // §12: the last minute of a run, for /moirai record

    // Selection gates and ranking (spec §4)
    public int MinTimeLeftSeconds { get; set; } = 180;
    public int MaxProgressPercent { get; set; } = 80;
    public int LevelMargin { get; set; } = 2;
    public int BossJoinProgress { get; set; } = 0;
    public int SpecialBossJoinProgress { get; set; } = 20;
    public List<SelectionCriterion> Priority { get; set; } = [.. DefaultPriority];
    public HashSet<uint> BlacklistedFates { get; set; } = [];
    public bool BonusOnly { get; set; } // A7: idle until a bonus fate is up

    // Movement (spec §7.1)
    public bool UseFlight { get; set; } = true;
    public float MountLegThreshold { get; set; } = 30f;
    public float ArriveTolerance { get; set; } = 4f;
    public float TeleportPenalty { get; set; } = 200f; // A12/C17: an aetheryte route must beat the direct path by this

    // Combat (spec §5, C10)
    public float MeleeRange { get; set; } = 2.5f;
    public float RangedRange { get; set; } = 8f;

    // Chocobo companion (release spec §6, F1–F10)
    public bool CompanionEnabled { get; set; } = true;
    public uint CompanionStanceId { get; set; } = 7; // Healer
    public int CompanionResummonBelowSeconds { get; set; } = 300;
    public bool CompanionStopWhenOutOfGreens { get; set; }

    public static readonly IReadOnlyList<SelectionCriterion> DefaultPriority =
        [SelectionCriterion.Progress, SelectionCriterion.Bonus, SelectionCriterion.TimeLeft, SelectionCriterion.DistanceTeleport];

    // The stored ladder, normalized: duplicates dropped, missing criteria appended in default order
    public List<SelectionCriterion> NormalizedPriority()
    {
        var all = Enum.GetValues<SelectionCriterion>();
        var order = Priority.Where(all.Contains).Distinct().ToList();
        order.AddRange(DefaultPriority.Where(c => !order.Contains(c)));
        order.AddRange(all.Where(c => !order.Contains(c)));
        return order;
    }

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
