using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Moirai.UI;

public sealed class ConfigWindow : Window
{
    private readonly Plugin _plugin;

    public ConfigWindow(Plugin plugin)
        : base("Moirai Settings###MoiraiConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var dirty = false;

        var mode = (int)c.Mode;
        if (ImGui.Combo("Mode", ref mode, "Yo-kai\0Single zone\0"))
        {
            c.Mode = (FarmMode)mode;
            dirty = true;
        }

        var flight = c.UseFlight;
        if (ImGui.Checkbox("Use flight", ref flight)) { c.UseFlight = flight; dirty = true; }

        var skipNpc = c.SkipNpcStartFates;
        if (ImGui.Checkbox("Skip NPC-started fates", ref skipNpc)) { c.SkipNpcStartFates = skipNpc; dirty = true; }

        var deathCap = c.DeathCap;
        if (ImGui.SliderInt("Stop after deaths", ref deathCap, 1, 10)) { c.DeathCap = deathCap; dirty = true; }

        var minTime = c.MinTimeLeftSeconds;
        if (ImGui.SliderInt("Min fate time left (s)", ref minTime, 60, 600)) { c.MinTimeLeftSeconds = minTime; dirty = true; }

        var maxProgress = c.MaxProgressPercent;
        if (ImGui.SliderInt("Max fate progress (%)", ref maxProgress, 10, 100)) { c.MaxProgressPercent = maxProgress; dirty = true; }

        var bossJoin = c.BossJoinProgress;
        if (ImGui.SliderInt("Boss fate join threshold (%)", ref bossJoin, 0, 90)) { c.BossJoinProgress = bossJoin; dirty = true; }

        if (dirty) c.Save();

        ImGui.Separator();
        ImGui.TextWrapped("Changes to mode and thresholds apply on the next Start.");
    }
}
