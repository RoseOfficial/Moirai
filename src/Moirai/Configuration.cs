using Dalamud.Configuration;

namespace Moirai;

public enum FarmMode { Yokai, SingleZone }

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public FarmMode Mode { get; set; } = FarmMode.Yokai;
    public bool UseFlight { get; set; } = true;
    public bool SkipNpcStartFates { get; set; } = true;
    public int DeathCap { get; set; } = 3;

    public int MinTimeLeftSeconds { get; set; } = 180;
    public int MaxProgressPercent { get; set; } = 80;
    public int BossJoinProgress { get; set; } = 0;
    public int SpecialBossJoinProgress { get; set; } = 20;

    // Empty = roster order
    public List<uint> YokaiPriority { get; set; } = [];

    public void Save() => Svc.PluginInterface.SavePluginConfig(this);
}
