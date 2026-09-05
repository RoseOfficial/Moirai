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
using Moirai.Core.Replay;
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
    private readonly TextAdvanceIpc _textAdvance;
    private readonly DodgeIpc _dodge;
    private readonly IntentExecutor _executor;
    private readonly Snapshot.SnapshotBuilder _snapshots;
    private Recorder? _recorder;   // §12: the last minute of the run, when the setting is on
    private bool _savedOnStop;

    public Configuration Config { get; }
    public Director? Director { get; private set; }
    public string LastStatus { get; private set; } = "idle";
    public StatusTimeline Timeline { get; private set; } = new();

    public bool IsRunning => Director is { } d && d.Phase is not (RunPhase.Idle or RunPhase.Stopped);
    public bool NavmeshReady => _navmesh.IsReady();
    public bool CombatBackendLoaded => _combat.RotationSolverInstalled;
    public bool? CombatBackendActive => _combat.IsActive();
    public bool TextAdvanceLoaded => _textAdvance.Installed;
    public bool DodgeLoaded => _dodge.Installed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _navmesh = new NavmeshIpc();
        _combat = new CombatIpc();
        _textAdvance = new TextAdvanceIpc();
        _dodge = new DodgeIpc();
        _executor = new IntentExecutor(_navmesh, _combat, _dodge, Config);
        _snapshots = new Snapshot.SnapshotBuilder(_navmesh, _combat, _textAdvance, _dodge, new Snapshot.AetheryteProjection(), [CompanionData.GysahlGreensItemId]);

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

        var settings = BuildSettings();
        IRandomSource random = SystemRandom.Instance;
        ILandingResolver landing = new NavmeshLanding(_navmesh);
        _recorder = null;
        if (Config.KeepRecording)
        {
            // §12: the recorder sits between the planner and its two services, so every input is on file
            _recorder = new Recorder(settings, Version, random, landing);
            random = _recorder.Random;
            landing = _recorder.Landing;
        }
        Timeline = new StatusTimeline();
        _savedOnStop = false;

        Director = DirectorFactory.Create(settings, module: null, random, landing, w => ZoneFlightAllowed(w.TerritoryId)); // the settings pick the module
        _executor.Reset();
        _textAdvance.Take(); // Talk and hand-in windows are TextAdvance's for the whole run
        Director.Start();
        _overlay.IsOpen = true;
    }

    // The run's settings, read once at Start. The blacklist stays a live reference so edits apply
    // at the next selection; a recording carries whatever it held when saved.
    private RunSettings BuildSettings() => new(
        new SelectionConfig
        {
            MinTimeLeftSeconds = Config.MinTimeLeftSeconds,
            MaxProgressPercent = Config.MaxProgressPercent,
            LevelMargin = Config.LevelMargin,
            BossJoinProgress = Config.BossJoinProgress,
            SpecialBossJoinProgress = Config.SpecialBossJoinProgress,
            Priority = Config.NormalizedPriority(),
            Blacklist = Config.BlacklistedFates,
            BonusOnly = Config.BonusOnly,
            SkipCollectFates = Config.SkipCollectFates,
            SkipNpcStartFates = Config.SkipNpcStartFates,
            TeleportPenalty = Config.TeleportPenalty, // one margin for the A12 ranking and the C17 leg
        },
        new MovementConfig
        {
            MountLegThreshold = Config.MountLegThreshold,
            ArriveTolerance = Config.ArriveTolerance,
            TeleportPenalty = Config.TeleportPenalty,
        },
        new EngageConfig
        {
            MeleeRange = Config.MeleeRange,
            RangedRange = Config.RangedRange,
        },
        new CompanionConfig
        {
            Enabled = Config.CompanionEnabled,
            GreensItemId = CompanionData.GysahlGreensItemId,
            StanceActionId = Config.CompanionStanceId,
            ResummonBelowSeconds = Config.CompanionResummonBelowSeconds,
            StopWhenOutOfGreens = Config.CompanionStopWhenOutOfGreens,
        },
        new DirectorConfig { DeathCap = Config.DeathCap },
        new RotationConfig { Zones = [.. Config.RotationZones], QuietSeconds = Config.RotateWhenQuietSeconds });

    private static bool ZoneFlightAllowed(ushort territory) => !ZoneData.NoFlyTerritories.Contains(territory);

    // Writes the rolling recording to the config directory and says so in chat; false when there is none
    public bool SaveRecording(string tag)
    {
        if (_recorder is not { Count: > 0 } recorder)
        {
            Svc.Chat.Print("[Moirai] Nothing to save: recording is off, or no run has happened yet.");
            return false;
        }
        var dir = Svc.PluginInterface.GetPluginConfigDirectory();
        var path = Path.Combine(dir, $"recording-{tag}-{DateTime.Now:yyyyMMdd-HHmmss}{RecordingFile.Extension}");
        try
        {
            Directory.CreateDirectory(dir);
            using var stream = File.Create(path);
            RecordingFile.Write(recorder.Snapshot(), stream);
        }
        catch (Exception e)
        {
            Svc.Log.Error(e, "could not save the recording");
            Svc.Chat.Print("[Moirai] Could not save the recording; see /xllog.");
            return false;
        }
        Svc.Chat.Print($"[Moirai] Recording saved to {path}");
        return true;
    }

    public void StopRun()
    {
        Director?.Stop(StopReason.UserRequested);
        _navmesh.Stop();
        _combat.Set(false, CombatMode.Auto);
        _combat.ResetCache();
        _dodge.SetAi(false);
        _dodge.Reset();
        _textAdvance.Release();
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

    private void OnUpdate(IFramework framework)
    {
        if (Director is not { } director || director.Phase is RunPhase.Idle or RunPhase.Stopped)
            return;
        if (!Svc.ClientState.IsLoggedIn)
            return;

        var snapshot = _snapshots.Build(director.CurrentFate?.Id);
        if (snapshot is null)
            return;

        _textAdvance.Reassert(); // it drops external control on its own after a zone change
        var output = _recorder is { } recorder
            ? recorder.Tick(director, snapshot, ZoneFlightAllowed(snapshot.TerritoryId))
            : director.Tick(snapshot);
        LastStatus = output.Status;
        Timeline.Observe(snapshot.NowEpoch, output.Status);
        _executor.Execute(output.Intent, snapshot);
        if (director.Phase == RunPhase.Stopped)
        {
            _textAdvance.Release(); // a stop the planner decided (death cap, dependency lost, stuck)
            if (director.StoppedBecause is { } why && why != StopReason.UserRequested && !_savedOnStop)
            {
                _savedOnStop = true;
                SaveRecording(why.ToString()); // the evidence, kept without being asked
            }
        }
    }

    private const string Usage = "Usage: /moirai [start|stop|config|debug|record|help]";

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
            case "record":
                SaveRecording("manual");
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
