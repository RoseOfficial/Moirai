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
using Moirai.Execution;
using Moirai.Game;
using Moirai.Ipc;
using Moirai.UI;

namespace Moirai;

public sealed class Plugin : IDalamudPlugin
{
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

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _navmesh = new NavmeshIpc();
        _combat = new CombatIpc();
        _executor = new IntentExecutor(_navmesh, _combat, Config);
        _snapshots = new Snapshot.SnapshotBuilder(Config, _navmesh);

        _overlay = new OverlayWindow(this);
        _configWindow = new ConfigWindow(this);
        _windows.AddWindow(_overlay);
        _windows.AddWindow(_configWindow);

        pluginInterface.UiBuilder.Draw += _windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += () => _overlay.IsOpen = true;
        pluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        Svc.Commands.AddHandler("/moirai", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Moirai window. '/moirai config' opens settings.",
        });

        Svc.Framework.Update += OnUpdate;
    }

    public void StartRun()
    {
        var selection = new SelectionConfig
        {
            MinTimeLeftSeconds = Config.MinTimeLeftSeconds,
            MaxProgressPercent = Config.MaxProgressPercent,
            BossJoinProgress = Config.BossJoinProgress,
            SpecialBossJoinProgress = Config.SpecialBossJoinProgress,
        };

        var engage = () => new EngageBehavior(new EngageConfig());
        Director = new Director(
            new SingleZoneModule(),
            selection,
            new DirectorConfig { DeathCap = Config.DeathCap },
            new TravelBehavior(new MovementConfig()),
            kind => kind switch
            {
                FateKind.Collect => new CollectBehavior(engage()),
                FateKind.NpcStart => new NpcStartBehavior(),
                FateKind.Escort => new EscortBehavior(engage()),
                _ => engage(),
            },
            BuildContext);
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

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
                _configWindow.IsOpen = !_configWindow.IsOpen;
                break;
            case "debug":
                Svc.Chat.Print($"[Moirai] status='{LastStatus}'");
                Svc.Chat.Print($"[Moirai] visible ui: {string.Join(", ", GameEx.VisibleAddonNames())}");
                break;
            default:
                _overlay.IsOpen = !_overlay.IsOpen;
                break;
        }
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
