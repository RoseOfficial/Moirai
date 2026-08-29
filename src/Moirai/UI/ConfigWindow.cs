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

        var autoEquip = c.AutoEquipWatch;
        if (ImGui.Checkbox("Auto-equip the Yo-kai Watch", ref autoEquip)) { c.AutoEquipWatch = autoEquip; dirty = true; }

        var autoBuy = c.AutoBuyMinions;
        if (ImGui.Checkbox("Auto-buy missing minions from Nohi", ref autoBuy)) { c.AutoBuyMinions = autoBuy; dirty = true; }

        var deathCap = c.DeathCap;
        if (ImGui.SliderInt("Stop after deaths", ref deathCap, 1, 10)) { c.DeathCap = deathCap; dirty = true; }

        var minTime = c.MinTimeLeftSeconds;
        if (ImGui.SliderInt("Min fate time left (s)", ref minTime, 60, 600)) { c.MinTimeLeftSeconds = minTime; dirty = true; }

        var maxProgress = c.MaxProgressPercent;
        if (ImGui.SliderInt("Max fate progress (%)", ref maxProgress, 10, 100)) { c.MaxProgressPercent = maxProgress; dirty = true; }

        var bossJoin = c.BossJoinProgress;
        if (ImGui.SliderInt("Boss fate join threshold (%)", ref bossJoin, 0, 90)) { c.BossJoinProgress = bossJoin; dirty = true; }

        ImGui.Separator();
        ImGui.TextUnformatted("Yo-kai priority (checked = farmed, top first)");
        var order = _plugin.FullPriorityOrder();
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var yokai = Data.YokaiData.Roster.First(y => y.MinionId == id);

            var enabled = !c.YokaiDisabled.Contains(id);
            if (ImGui.Checkbox($"##yk-en-{id}", ref enabled))
            {
                if (enabled) c.YokaiDisabled.Remove(id);
                else c.YokaiDisabled.Add(id);
                dirty = true;
            }
            ImGui.SameLine();
            if (ImGui.ArrowButton($"##yk-up-{id}", ImGuiDir.Up) && i > 0)
            {
                (order[i - 1], order[i]) = (order[i], order[i - 1]);
                c.YokaiPriority = order;
                dirty = true;
            }
            ImGui.SameLine();
            if (ImGui.ArrowButton($"##yk-dn-{id}", ImGuiDir.Down) && i < order.Count - 1)
            {
                (order[i + 1], order[i]) = (order[i], order[i + 1]);
                c.YokaiPriority = order;
                dirty = true;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(yokai.Name);
        }

        if (dirty) c.Save();

        ImGui.Separator();
        ImGui.TextWrapped("Changes to mode, thresholds, and priority apply on the next Start.");
    }
}
