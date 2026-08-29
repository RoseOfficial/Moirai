using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Moirai.Core;

namespace Moirai.UI;

public sealed class OverlayWindow : Window
{
    private readonly Plugin _plugin;

    public OverlayWindow(Plugin plugin)
        : base("Moirai###MoiraiOverlay", ImGuiWindowFlags.AlwaysAutoResize)
    {
        _plugin = plugin;
    }

    public override void Draw()
    {
        var d = _plugin.Director;
        var running = d is not null && d.Phase is not (RunPhase.Idle or RunPhase.Stopped);

        if (!running)
        {
            if (ImGui.Button("Start"))
                _plugin.StartRun();
        }
        else
        {
            if (ImGui.Button("Stop"))
                _plugin.StopRun();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted(d is null ? "idle" : d.Phase.ToString());

        ImGui.Separator();
        ImGui.TextUnformatted(_plugin.LastStatus);

        if (d is not null)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.9f, 0.6f, 1f),
                $"done {d.Ledger.Completed}  failed {d.Ledger.Failed}  abandoned {d.Ledger.Abandoned}  deaths {d.Ledger.Deaths}");
            if (d.CurrentFate is { } fate)
                ImGui.TextUnformatted($"fate {fate.Id}  {fate.Progress}%  {fate.EffectiveTimeLeft}s left");
            if (d.StoppedBecause is { } why)
                ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.6f, 1f), $"stopped: {why}");
        }

        foreach (var line in _plugin.YokaiProgressLines())
            ImGui.TextUnformatted(line);
    }
}
