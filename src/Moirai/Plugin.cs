using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Moirai.Core;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;
using Moirai.Data;
using Moirai.Diagnostics;
using Moirai.Execution;
using Moirai.Game;
using Moirai.Ipc;
using Moirai.UI;

namespace Moirai;

public sealed class Plugin : IDalamudPlugin
{
    public const string RepoUrl = "https://github.com/RoseOfficial/Moirai";
    public const string InstallRepoUrl = "https://raw.githubusercontent.com/RoseOfficial/Olympus/main/repo.json";
    public static string Version { get; } = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "dev";

    private readonly WindowSystem _windows = new("Moirai");
    private readonly OverlayWindow _overlay;
    private readonly ConfigWindow _configWindow;
    private readonly NavmeshIpc _navmesh;
    private readonly CombatIpc _combat;
    private readonly IntentExecutor _executor;
    private readonly Snapshot.SnapshotBuilder _snapshots;

    public Configuration Config { get; }
    public Director? Director { get; private set; }
    public string LastStatus { get; private set; } = "idle";

    public bool IsRunning => Director is { } d && d.Phase is not (RunPhase.Idle or RunPhase.Stopped);
    public bool NavmeshReady => _navmesh.IsReady();
    public bool CombatBackendLoaded => _combat.RotationSolverInstalled;
    public bool? CombatBackendActive => _combat.IsActive();

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _navmesh = new NavmeshIpc();
        _combat = new CombatIpc();
        _executor = new IntentExecutor(_navmesh, _combat, Config);
        _snapshots = new Snapshot.SnapshotBuilder(Config, _navmesh, [CompanionData.GysahlGreensItemId]);

        _overlay = new OverlayWindow(this);
        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_overlay);
        _windows.AddWindow(_configWindow);

        pluginInterface.UiBuilder.Draw += _windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += () => _overlay.IsOpen = true;
        pluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        Svc.Commands.AddHandler("/moirai", new CommandInfo(OnCommand)
        {
            HelpMessage = "FATE farming. Opens the window; try /moirai help for the full command list.",
        });

        Svc.Framework.Update += OnUpdate;
        Svc.Log.Information("[Moirai] Loaded.");
    }

    public void OpenSettings() => _configWindow.IsOpen = true;

    public void StartRun()
    {
        if (IsRunning) return;

        var selection = new SelectionConfig
        {
            MinTimeLeftSeconds = Config.MinTimeLeftSeconds,
            MaxProgressPercent = Config.MaxProgressPercent,
            LevelMargin = Config.LevelMargin,
            BossJoinProgress = Config.BossJoinProgress,
            SpecialBossJoinProgress = Config.SpecialBossJoinProgress,
            Priority = Config.NormalizedPriority(),
            Blacklist = Config.BlacklistedFates, // live reference: edits apply at the next selection
        };
        var movement = new MovementConfig
        {
            MountLegThreshold = Config.MountLegThreshold,
            ArriveTolerance = Config.ArriveTolerance,
        };
        var engageConfig = new EngageConfig
        {
            MeleeRange = Config.MeleeRange,
            RangedRange = Config.RangedRange,
        };

        var companion = new CompanionUpkeep(new CompanionConfig
        {
            Enabled = Config.CompanionEnabled,
            GreensItemId = CompanionData.GysahlGreensItemId,
            StanceActionId = Config.CompanionStanceId,
            ResummonBelowSeconds = Config.CompanionResummonBelowSeconds,
            StopWhenOutOfGreens = Config.CompanionStopWhenOutOfGreens,
        });

        var engage = () => new EngageBehavior(engageConfig);
        Director = new Director(
            new SingleZoneModule(),
            selection,
            new DirectorConfig { DeathCap = Config.DeathCap },
            new TravelBehavior(movement),
            kind => kind switch
            {
                FateKind.Collect => new CollectBehavior(engage()),
                FateKind.NpcStart => new NpcStartBehavior(),
                FateKind.Escort => new EscortBehavior(engage()),
                _ => engage(),
            },
            BuildContext,
            companion,
            new StrayAggroClear(engageConfig));
        _executor.Reset();
        Director.Start();
        _overlay.IsOpen = true;
    }

    public void StopRun()
    {
        Director?.Stop(StopReason.UserRequested);
        _navmesh.Stop();
        _combat.Set(false, CombatMode.Auto);
        _combat.ResetCache();
    }

    // Display only: the planner keys everything by fate id
    public static string? FateName(uint fateId)
    {
        foreach (var fate in Svc.Fates)
            if (fate is not null && fate.FateId == fateId)
                return fate.Name.TextValue;
        return null;
    }

    public static string? FateNameFromSheet(uint fateId)
    {
        try
        {
            var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>()?.GetRowOrDefault(fateId);
            var name = row?.Name.ExtractText();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private BehaviorContext BuildContext(WorldSnapshot w)
        => new(
            Fate: null,
            ZoneFlightAllowed: !ZoneData.NoFlyTerritories.Contains(w.TerritoryId),
            Random: SystemRandom.Instance,
            Landing: new NavmeshLanding(_navmesh));

    private void OnUpdate(IFramework framework)
    {
        if (Director is not { } director || director.Phase is RunPhase.Idle or RunPhase.Stopped)
            return;
        if (!Svc.ClientState.IsLoggedIn)
            return;

        var snapshot = _snapshots.Build(director.CurrentFate?.Id);
        if (snapshot is null)
            return;

        var output = director.Tick(snapshot);
        LastStatus = output.Status;
        _executor.Execute(output.Intent, snapshot);
    }

    private const string Usage = "Usage: /moirai [start|stop|config|debug|help]";

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                _overlay.IsOpen = !_overlay.IsOpen;
                break;
            case "start":
                StartRun();
                Svc.Chat.Print("[Moirai] Started.");
                break;
            case "stop":
                StopRun();
                Svc.Chat.Print("[Moirai] Stopped.");
                break;
            case "config":
            case "settings":
                _configWindow.IsOpen = !_configWindow.IsOpen;
                break;
            case "debug":
                CopyDebugReport();
                break;
            default:
                Svc.Chat.Print($"[Moirai] {Usage}");
                break;
        }
    }

    // Copies a plain-text dump of the zone's fates and Moirai's view of them for bug reports
    public void CopyDebugReport()
    {
        string report;
        try { report = DebugReport.Build(this, _snapshots); }
        catch (Exception e)
        {
            Svc.Log.Error(e, "debug report failed");
            Svc.Chat.Print("[Moirai] Debug report failed; see /xllog.");
            return;
        }
        var path = Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "debug-report.txt");
        try { File.WriteAllText(path, report); }
        catch (Exception e) { Svc.Log.Warning(e, "could not save debug report"); path = "(not saved)"; }
        try { ImGui.SetClipboardText(report); }
        catch (Exception e) { Svc.Log.Warning(e, "clipboard unavailable"); }
        Svc.Chat.Print($"[Moirai] Debug report copied to the clipboard and saved to {path}");
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.Commands.RemoveHandler("/moirai");
        Svc.PluginInterface.UiBuilder.Draw -= _windows.Draw;
        _windows.RemoveAllWindows();
        StopRun();
    }
}

public sealed class SystemRandom : IRandomSource
{
    public static readonly SystemRandom Instance = new();
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class NavmeshLanding(NavmeshIpc navmesh) : ILandingResolver
{
    public System.Numerics.Vector3? ResolveFloor(System.Numerics.Vector3 near)
        => navmesh.PointOnFloor(near);
}
