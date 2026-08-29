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
    private WorldSnapshot? _lastSnapshot;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();
        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _navmesh = new NavmeshIpc();
        _combat = new CombatIpc();
        _executor = new IntentExecutor(_navmesh, _combat, Config, new MinionPurchaser(_navmesh));
        _snapshots = new Snapshot.SnapshotBuilder(
            Config, _navmesh, YokaiData.TrackedItemIds, YokaiData.WatchItemId,
            YokaiData.Roster.Select(y => y.MinionId).ToList());

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

        IFarmModule module = Config.Mode == FarmMode.Yokai
            ? new YokaiModule(
                YokaiData.Roster,
                FullPriorityOrder().Where(id => !Config.YokaiDisabled.Contains(id)).ToList(),
                medalItemId: YokaiData.MedalItemId,
                autoBuy: Config.AutoBuyMinions)
            : new SingleZoneModule();

        var engage = () => new EngageBehavior(new EngageConfig());
        Director = new Director(
            module,
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

    public IEnumerable<string> YokaiProgressLines()
    {
        if (Config.Mode != FarmMode.Yokai || _lastSnapshot is not { } w)
            yield break;
        yield return $"Yo-kai Medals: {w.CountOf(YokaiData.MedalItemId)}";
        foreach (var yokai in YokaiData.Roster)
        {
            var count = w.CountOf(yokai.LegendaryMedalItemId);
            yield return $"{yokai.Name}: {count}/10";
        }
    }

    public bool WatchOwnedButUnequipped()
        => Config.Mode == FarmMode.Yokai
           && _lastSnapshot is { Player.YokaiWatchOwned: true }
           && !Game.GameEx.IsItemEquipped(YokaiData.WatchItemId);

    // The stored order, normalized: unknown ids dropped, missing roster ids appended
    public List<uint> FullPriorityOrder()
    {
        var rosterIds = YokaiData.Roster.Select(y => y.MinionId).ToList();
        var order = Config.YokaiPriority.Where(rosterIds.Contains).ToList();
        order.AddRange(rosterIds.Where(id => !order.Contains(id)));
        return order;
    }

    private void MaybeAutoEquipWatch(WorldSnapshot w)
    {
        if (Config.Mode != FarmMode.Yokai || !Config.AutoEquipWatch) return;
        if (!w.Player.YokaiWatchOwned || w.Player.InCombat || w.Player.IsMounted) return;
        if (Game.GameEx.IsItemEquipped(YokaiData.WatchItemId)) return;
        if (Throttle.Try("moirai.autoequip", 10000))
            Game.GameEx.EquipWristItem(YokaiData.WatchItemId);
    }

    private BehaviorContext BuildContext(WorldSnapshot w)
        => new(
            Fate: null,
            ZoneFlightAllowed: !YokaiData.NoFlyTerritories.Contains(w.TerritoryId),
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
        _lastSnapshot = snapshot;

        MaybeAutoEquipWatch(snapshot);

        var output = director.Tick(snapshot);
        LastStatus = _executor.PurchaseStatus ?? output.Status;
        _executor.Execute(output.Intent, snapshot);
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            _configWindow.IsOpen = !_configWindow.IsOpen;
        else
            _overlay.IsOpen = !_overlay.IsOpen;
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
