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
    private readonly MinionPurchaser _purchaser;
    private readonly GearRepairer _repairer;
    private readonly IntentExecutor _executor;
    private readonly Snapshot.SnapshotBuilder _snapshots;
    private Recorder? _recorder;   // §12: the last six minutes of the run, when the setting is on
    private bool _savedOnStop;

    // The planner ticks ten times a second, not every frame: its windows are measured in
    // milliseconds and every intent is idempotent, and the recording's fixed number of frames then
    // covers the same stretch of the run whatever the frame rate (§12)
    private const long PlanIntervalMs = 100;
    private long _nextPlanMs;

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
    public MinionPurchaser Purchaser => _purchaser;
    public GearRepairer Repairer => _repairer;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _navmesh = new NavmeshIpc();
        _combat = new CombatIpc();
        _textAdvance = new TextAdvanceIpc();
        _dodge = new DodgeIpc();
        _purchaser = new MinionPurchaser(_navmesh, Config);
        _repairer = new GearRepairer(_navmesh);
        _executor = new IntentExecutor(_navmesh, _combat, _dodge, _purchaser, _repairer, Config);
        _snapshots = new Snapshot.SnapshotBuilder(_navmesh, _combat, _textAdvance, _dodge, new Snapshot.AetheryteProjection(),
            [CompanionData.GysahlGreensItemId, .. YokaiData.TrackedItemIds], YokaiData.WatchItemId, YokaiData.MinionIds, _purchaser,
            _repairer, GearData.DarkMatterGrades);

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
        _nextPlanMs = 0;
        _purchaser.Reset();
        _repairer.Reset();

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
        new DirectorConfig { DeathCap = Config.DeathCap, Sprint = Config.Sprint },
        new RotationConfig { Zones = [.. Config.RotationZones], QuietSeconds = Config.RotateWhenQuietSeconds },
        new YokaiConfig
        {
            Enabled = Config.YokaiEnabled,
            Roster = YokaiData.Roster,
            Priority = [.. Config.YokaiPriority],
            LegendaryCap = Config.YokaiCap,
            WatchItemId = YokaiData.WatchItemId,
            AutoEquipWatch = Config.YokaiAutoEquipWatch,
            QuietSeconds = Config.RotateWhenQuietSeconds,
            MedalItemId = YokaiData.MedalItemId,
            AutoBuy = Config.YokaiAutoBuy,
        },
        new GearConfig
        {
            Enabled = Config.RepairEnabled,
            RepairBelowPercent = Config.RepairBelowPercent,
            StopWhenBroken = Config.StopWhenGearBroken,
        });

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

    public bool IsPaused => Director is { Phase: RunPhase.Paused };

    // A pause hands TextAdvance back and drops a purchase or a repair under way, so NPCs can be
    // talked to by hand; resume takes TextAdvance again, and the planner asks for either anew
    public void PauseRun()
    {
        Director?.Pause();
        if (!IsPaused) return;
        if (_purchaser.IsActive) _purchaser.Reset();
        if (_repairer.IsActive) _repairer.Reset();
        _textAdvance.Release();
    }

    public void ResumeRun()
    {
        if (!IsPaused) return;
        Director!.Resume();
        _textAdvance.Take();
    }

    public void StopRun()
    {
        Director?.Stop(StopReason.UserRequested);
        StandDown();
    }

    // Everything Moirai drives, handed back: the path, the rotation, the dodge AI, a purchase, a
    // repair, TextAdvance
    private void StandDown()
    {
        _navmesh.Stop();
        _combat.Set(false, CombatMode.Auto);
        _combat.ResetCache();
        _dodge.SetAi(false);
        _dodge.Reset();
        _purchaser.Reset();
        _repairer.Reset();
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

        try
        {
            Step(director);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private void Step(Director director)
    {
        if (director.Phase != RunPhase.Paused)
            _textAdvance.Reassert(); // it drops external control on its own after a zone change; a pause hands it back
        if (_purchaser.IsActive)
        {
            // E9: a purchase is all game windows, so it runs every frame, outside the planner's busy
            // guard; the planner keeps asking for it and its intent is a no-op until the purchase ends
            _purchaser.Tick();
            LastStatus = _purchaser.Status;
            Timeline.Observe(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LastStatus);
            return;
        }
        if (_repairer.IsActive)
        {
            // R1: the Repair window is worked the same way, every frame, while the planner waits
            _repairer.Tick();
            LastStatus = _repairer.Status;
            Timeline.Observe(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), LastStatus);
            return;
        }

        var now = Environment.TickCount64;
        if (now < _nextPlanMs)
            return;
        _nextPlanMs = now + PlanIntervalMs;

        var snapshot = _snapshots.Build(director.CurrentFate?.Id);
        if (snapshot is null)
            return;

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

    // Whatever a tick throws stops the run where it stands, instead of throwing again every frame
    // while vnavmesh and the rotation carry on with their last orders. The recording keeps the tick
    // that threw when the planner was the one to throw.
    private void Fail(Exception e)
    {
        Svc.Log.Error(e, "[Moirai] a tick threw; the run is stopped");
        Director?.Stop(StopReason.InternalError);
        try { StandDown(); }
        catch (Exception inner) { Svc.Log.Error(inner, "[Moirai] standing down after the error failed too"); }
        LastStatus = $"stopped: {Recorder.ExceptionStatus(e)}";
        Svc.Chat.PrintError("[Moirai] Stopped on an internal error; see /xllog.");
        if (!_savedOnStop)
        {
            _savedOnStop = true;
            SaveRecording(nameof(StopReason.InternalError));
        }
    }

    private const string Usage = "Usage: /moirai [start|pause|resume|stop|config|debug|record|help]";

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
            case "pause":
                PauseRun();
                Svc.Chat.Print(IsPaused ? "[Moirai] Paused; /moirai resume picks the session back up." : "[Moirai] Nothing to pause.");
                break;
            case "resume":
                ResumeRun();
                Svc.Chat.Print(IsRunning && !IsPaused ? "[Moirai] Resumed." : "[Moirai] Nothing to resume.");
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
