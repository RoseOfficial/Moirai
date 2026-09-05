using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Moirai.Core;

namespace Moirai.UI;

public sealed class OverlayWindow : Window
{
    private static readonly Vector4 Muted = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 Good = new(0.6f, 0.9f, 0.6f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 Bad = new(0.9f, 0.6f, 0.6f, 1f);

    private readonly Plugin _plugin;

    public OverlayWindow(Plugin plugin)
        : base($"Moirai {Plugin.Version}###MoiraiOverlay", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
    }

    public override void Draw()
    {
        var d = _plugin.Director;
        var running = _plugin.IsRunning;

        if (!running)
        {
            if (ImGui.Button("Start")) _plugin.StartRun();
        }
        else
        {
            if (ImGui.Button("Stop")) _plugin.StopRun();
        }
        ImGui.SameLine();
        if (ImGui.Button("Settings")) _plugin.OpenSettings();
        ImGui.SameLine();
        ImGui.TextColored(Muted, d is null ? "idle" : Phase(d.Phase));

        if (!running)
        {
            if (!_plugin.NavmeshReady)
                ImGui.TextColored(Warn, "vnavmesh is not ready: nothing will move until it is.");
            if (!_plugin.CombatBackendLoaded)
                ImGui.TextColored(Warn, "RotationSolver Reborn is not loaded: fates will not be fought.");
            if (!_plugin.TextAdvanceLoaded)
                ImGui.TextColored(Warn, "TextAdvance is not loaded: NPC dialogue and hand-ins will not advance.");
        }

        ImGui.Separator();
        ImGui.TextUnformatted(_plugin.LastStatus);
        if (_plugin.Config.RotationZones.Count > 0)
            ImGui.TextColored(Muted, $"zone: {Data.ZoneNames.Name((ushort)Svc.ClientState.TerritoryType)}");

        if (d is null) return;

        if (d.CurrentFate is { } fate)
        {
            var name = Plugin.FateName(fate.Id);
            ImGui.TextUnformatted(name is null ? $"Fate {fate.Id}" : name);
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"{(fate.IsBonus ? "bonus " : "")}{fate.Kind}  {fate.Progress}%  {fate.EffectiveTimeLeft}s left");
        }

        ImGui.TextColored(Good,
            $"done {d.Ledger.Completed}   failed {d.Ledger.Failed}   abandoned {d.Ledger.Abandoned}   deaths {d.Ledger.Deaths}");
        ImGui.TextColored(Muted, $"{Elapsed(d.Ledger.ElapsedSeconds)}   {d.Ledger.CompletedPerHour:0.0} fates/h");

        if (d.Companion?.Note is { } companionNote)
            ImGui.TextColored(Warn, $"Companion: {companionNote}");

        if (d.StoppedBecause is { } why)
            ImGui.TextColored(Bad, $"Stopped: {Reason(why)}");
    }

    private static string Elapsed(long seconds)
        => seconds >= 3600
            ? $"{seconds / 3600}h {seconds % 3600 / 60:00}m"
            : $"{seconds / 60}m {seconds % 60:00}s";

    private static string Phase(RunPhase phase) => phase switch
    {
        RunPhase.SelectingFate => "selecting",
        RunPhase.Traveling => "traveling",
        RunPhase.InFate => "in fate",
        RunPhase.WaitingContinuation => "waiting for continuation",
        RunPhase.Stopped => "stopped",
        _ => "idle",
    };

    private static string Reason(Core.Intents.StopReason reason) => reason switch
    {
        Core.Intents.StopReason.UserRequested => "stopped by you",
        Core.Intents.StopReason.StuckExhausted => "stuck and out of recovery options",
        Core.Intents.StopReason.DeathCapReached => "death cap reached",
        Core.Intents.StopReason.OutOfGreens => "out of Gysahl Greens",
        Core.Intents.StopReason.DependencyLost => "a required plugin went away",
        Core.Intents.StopReason.DataMissing => "missing data",
        Core.Intents.StopReason.SessionComplete => "session complete",
        Core.Intents.StopReason.ZonesUnreachable => "none of the listed zones could be reached",
        _ => reason.ToString(),
    };
}
