using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Moirai.Core.Planning;
using Moirai.Data;

namespace Moirai.UI;

public sealed class ConfigWindow : Window
{
    private static readonly Vector4 Muted = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 Good = new(0.6f, 0.9f, 0.6f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.7f, 0.2f, 1f);

    private readonly Plugin _plugin;
    private int _blacklistInput;

    public ConfigWindow(Plugin plugin)
        : base("Moirai Settings###MoiraiConfig")
    {
        _plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(800, 1000),
        };
    }

    public override void Draw()
    {
        var c = _plugin.Config;
        var dirty = false;

        if (ImGui.BeginTabBar("###MoiraiTabs"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneral(c, ref dirty);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Selection"))
            {
                DrawSelection(c, ref dirty);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Movement"))
            {
                DrawMovement(c, ref dirty);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Combat"))
            {
                DrawCombat(c, ref dirty);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("About"))
            {
                DrawAbout();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        if (dirty) c.Save();
    }

    private void DrawGeneral(Configuration c, ref bool dirty)
    {
        var deathCap = c.DeathCap;
        if (ImGui.SliderInt("Stop after deaths", ref deathCap, 1, 10)) { c.DeathCap = deathCap; dirty = true; }
        Hint("The run stops with a reason once this many deaths are recorded in one session.");

        var skipNpc = c.SkipNpcStartFates;
        if (ImGui.Checkbox("Skip NPC-started fates", ref skipNpc)) { c.SkipNpcStartFates = skipNpc; dirty = true; }
        Hint("Unopened fates that need a starter NPC, collect fates included, are left alone when checked.");

        var record = c.KeepRecording;
        if (ImGui.Checkbox("Keep a rolling recording for bug reports", ref record)) { c.KeepRecording = record; dirty = true; }
        Hint("The last minute of what Moirai saw and decided, in memory only. /moirai record saves it, and a run that stops on its own saves it too. Applies on the next Start.");

        ImGui.Separator();
        ImGui.TextColored(Muted, "Threshold and ladder changes apply on the next Start. Blacklist changes apply at the next selection.");
    }

    private void DrawSelection(Configuration c, ref bool dirty)
    {
        ImGui.TextUnformatted("Gates");
        var minTime = c.MinTimeLeftSeconds;
        if (ImGui.SliderInt("Min time left (s)", ref minTime, 60, 600)) { c.MinTimeLeftSeconds = minTime; dirty = true; }
        var maxProgress = c.MaxProgressPercent;
        if (ImGui.SliderInt("Max progress (%)", ref maxProgress, 10, 100)) { c.MaxProgressPercent = maxProgress; dirty = true; }
        var margin = c.LevelMargin;
        if (ImGui.SliderInt("Level margin", ref margin, 0, 10)) { c.LevelMargin = margin; dirty = true; }
        Hint("Fates above your level plus this margin are skipped.");
        var bossJoin = c.BossJoinProgress;
        if (ImGui.SliderInt("Boss join threshold (%)", ref bossJoin, 0, 90)) { c.BossJoinProgress = bossJoin; dirty = true; }
        var specialJoin = c.SpecialBossJoinProgress;
        if (ImGui.SliderInt("Special boss join threshold (%)", ref specialJoin, 0, 90)) { c.SpecialBossJoinProgress = specialJoin; dirty = true; }
        Hint("Boss fates are joined only once their progress reaches the threshold; 0% joins from the start.");
        Hint("Special bosses are the achievement and world bosses with the big-boss banner, such as Lazy for You.");

        ImGui.Separator();
        ImGui.TextUnformatted("Ranking ladder (compared top to bottom; the first difference decides)");
        var order = c.NormalizedPriority();
        for (var i = 0; i < order.Count; i++)
        {
            var crit = order[i];
            if (ImGui.ArrowButton($"###crit-up-{crit}", ImGuiDir.Up) && i > 0)
            {
                (order[i - 1], order[i]) = (order[i], order[i - 1]);
                c.Priority = order;
                dirty = true;
            }
            ImGui.SameLine();
            if (ImGui.ArrowButton($"###crit-dn-{crit}", ImGuiDir.Down) && i < order.Count - 1)
            {
                (order[i + 1], order[i]) = (order[i], order[i + 1]);
                c.Priority = order;
                dirty = true;
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(Label(crit));
        }
        if (ImGui.SmallButton("Reset ladder"))
        {
            c.Priority = [.. Configuration.DefaultPriority];
            dirty = true;
        }
        var bonusOnly = c.BonusOnly;
        if (ImGui.Checkbox("Bonus FATEs only", ref bonusOnly)) { c.BonusOnly = bonusOnly; dirty = true; }
        Hint("Idles until a FATE with the bonus marker is up instead of roaming. Bonus FATEs pay out more experience, gil, seals, and gemstones.");

        ImGui.Separator();
        ImGui.TextUnformatted("Blacklist (by fate id)");
        ImGui.SetNextItemWidth(120);
        ImGui.InputInt("###blacklist-input", ref _blacklistInput, 0, 0);
        ImGui.SameLine();
        if (ImGui.Button("Add") && _blacklistInput > 0)
        {
            c.BlacklistedFates.Add((uint)_blacklistInput);
            _blacklistInput = 0;
            dirty = true;
        }
        if (_plugin.Director?.CurrentFate is { } current)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Add current ({current.Id})"))
            {
                c.BlacklistedFates.Add(current.Id);
                dirty = true;
            }
        }
        uint? remove = null;
        foreach (var id in c.BlacklistedFates.OrderBy(x => x))
        {
            if (ImGui.SmallButton($"Remove###bl-{id}")) remove = id;
            ImGui.SameLine();
            var name = Plugin.FateNameFromSheet(id);
            ImGui.TextUnformatted(name is null ? $"{id}" : $"{id}  {name}");
        }
        if (remove is { } r)
        {
            c.BlacklistedFates.Remove(r);
            dirty = true;
        }
        if (c.BlacklistedFates.Count == 0)
            ImGui.TextColored(Muted, "No fates blacklisted.");
    }

    private static void DrawMovement(Configuration c, ref bool dirty)
    {
        var flight = c.UseFlight;
        if (ImGui.Checkbox("Use flight", ref flight)) { c.UseFlight = flight; dirty = true; }
        Hint("Flight is still refused in zones whose geometry breaks flight pathing.");

        var mountLeg = c.MountLegThreshold;
        if (ImGui.SliderFloat("Mount when the leg exceeds (y)", ref mountLeg, 10f, 100f, "%.0f")) { c.MountLegThreshold = mountLeg; dirty = true; }
        Hint("Shorter legs are walked; mounting is skipped in combat.");

        var arrive = c.ArriveTolerance;
        if (ImGui.SliderFloat("Arrival tolerance (y)", ref arrive, 2f, 10f, "%.1f")) { c.ArriveTolerance = arrive; dirty = true; }

        var penalty = c.TeleportPenalty;
        if (ImGui.SliderFloat("Teleport when it saves more than (y)", ref penalty, 50f, 1000f, "%.0f")) { c.TeleportPenalty = penalty; dirty = true; }
        Hint("A leg starts with a teleport to an attuned aetheryte when that beats the ride by this much. The teleport-aware ranking rung uses the same margin.");
    }

    private void DrawCombat(Configuration c, ref bool dirty)
    {
        ImGui.TextUnformatted("Backend: RotationSolver Reborn");
        ImGui.SameLine();
        if (_plugin.CombatBackendLoaded) ImGui.TextColored(Good, "loaded");
        else ImGui.TextColored(Warn, "not loaded");
        Hint("Moirai drives the rotation through the /rotation command family. Install RotationSolver Reborn so fates get fought.");

        ImGui.Separator();
        var melee = c.MeleeRange;
        if (ImGui.SliderFloat("Melee stop distance (y)", ref melee, 1.5f, 4f, "%.1f")) { c.MeleeRange = melee; dirty = true; }
        Hint("Distance from the target hitbox where a tank or melee job stops. Larger values break auto-attack range.");
        var ranged = c.RangedRange;
        if (ImGui.SliderFloat("Ranged stop distance (y)", ref ranged, 5f, 20f, "%.1f")) { c.RangedRange = ranged; dirty = true; }

        ImGui.Separator();
        ImGui.TextUnformatted("Chocobo companion");
        var companion = c.CompanionEnabled;
        if (ImGui.Checkbox("Keep the companion summoned", ref companion)) { c.CompanionEnabled = companion; dirty = true; }
        Hint("Uses Gysahl Greens when the companion is missing or its timer runs low, never while mounted or in combat.");
        if (!c.CompanionEnabled) return;

        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("Stance", CompanionData.StanceName(c.CompanionStanceId)))
        {
            foreach (var stance in CompanionData.Stances)
                if (ImGui.Selectable(stance.Name, stance.ActionId == c.CompanionStanceId))
                {
                    c.CompanionStanceId = stance.ActionId;
                    dirty = true;
                }
            ImGui.EndCombo();
        }
        Hint("Healer keeps you alive at any rank; Defender lets a low-rank chocobo hold aggro for squishy jobs.");

        var minutes = Math.Max(1, c.CompanionResummonBelowSeconds / 60);
        if (ImGui.SliderInt("Top up when under (min)", ref minutes, 1, 15)) { c.CompanionResummonBelowSeconds = minutes * 60; dirty = true; }
        Hint("Another green is used once the remaining time drops below this.");

        var stopOut = c.CompanionStopWhenOutOfGreens;
        if (ImGui.Checkbox("Stop the run when out of greens", ref stopOut)) { c.CompanionStopWhenOutOfGreens = stopOut; dirty = true; }
        Hint("Off: the run carries on without a companion and the overlay says why.");
    }

    private void DrawAbout()
    {
        ImGui.TextUnformatted($"Moirai {Plugin.Version}");
        ImGui.TextColored(Muted, "Automated FATE farming with a pure, unit-tested decision core.");
        ImGui.Separator();

        ImGui.TextUnformatted("Required plugins");
        Dependency("vnavmesh", _plugin.NavmeshReady, "ready", "not ready or not installed");
        Dependency("RotationSolver Reborn", _plugin.CombatBackendLoaded, "loaded", "not loaded");
        Dependency("TextAdvance", _plugin.TextAdvanceLoaded, "loaded", "not loaded");
        Hint("TextAdvance advances NPC dialogue and drives the collect hand-in window while a run is on; Moirai confirms the FATE-start prompt itself.");

        ImGui.Separator();
        ImGui.TextUnformatted("Commands");
        ImGui.TextColored(Muted, "/moirai            toggle the overlay");
        ImGui.TextColored(Muted, "/moirai start      start farming here");
        ImGui.TextColored(Muted, "/moirai stop       stop the run");
        ImGui.TextColored(Muted, "/moirai config     open settings");
        ImGui.TextColored(Muted, "/moirai debug      copy a debug report for bug reports");
        ImGui.TextColored(Muted, "/moirai record     save the last minute of the run for bug reports");

        ImGui.Separator();
        ImGui.TextUnformatted("Source and issues");
        if (ImGui.SmallButton("Copy repository link")) ImGui.SetClipboardText(Plugin.RepoUrl);
        ImGui.SameLine();
        ImGui.TextColored(Muted, Plugin.RepoUrl);
        ImGui.TextColored(Muted, "Ships through the Olympus plugin repository.");
        if (ImGui.SmallButton("Copy debug report")) _plugin.CopyDebugReport();
        ImGui.SameLine();
        ImGui.TextColored(Muted, "Paste it into a bug report: every fate in the zone, how Moirai reads it, and a status timeline.");
        if (ImGui.SmallButton("Save recording")) _plugin.SaveRecording("manual");
        ImGui.SameLine();
        ImGui.TextColored(Muted, "Attach it to a bug report: the last minute of the run, replayable in Moirai's tests.");
    }

    private static void Dependency(string name, bool ok, string okText, string badText)
    {
        ImGui.Bullet();
        ImGui.TextUnformatted(name);
        ImGui.SameLine();
        if (ok) ImGui.TextColored(Good, okText);
        else ImGui.TextColored(Warn, badText);
    }

    private static void Hint(string text)
    {
        ImGui.Indent();
        ImGui.TextColored(Muted, text);
        ImGui.Unindent();
    }

    private static string Label(SelectionCriterion crit) => crit switch
    {
        SelectionCriterion.Progress => "Progress: furthest along first",
        SelectionCriterion.Bonus => "Bonus fates first",
        SelectionCriterion.TimeLeft => "Time left: most remaining first",
        SelectionCriterion.Distance => "Distance: nearest first",
        SelectionCriterion.DistanceTeleport => "Distance, teleport-aware",
        _ => crit.ToString(),
    };
}
