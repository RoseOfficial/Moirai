using Moirai.Core.Behaviors;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;

namespace Moirai.Core.Replay;

// The one way a Director is assembled, shared by the plugin and the replayer so a recording
// rebuilds exactly the planner that made it (§12)
public static class DirectorFactory
{
    // The module the settings call for: rotation when zones are listed, otherwise the single zone
    public static IFarmModule CreateModule(RunSettings settings)
        => settings.Rotation is { Zones.Count: > 0 } rotation
            ? new ZoneRotationModule(rotation)
            : new SingleZoneModule();

    public static Director Create(
        RunSettings settings,
        IFarmModule? module,
        IRandomSource random,
        ILandingResolver landing,
        Func<WorldSnapshot, bool>? zoneFlightAllowed = null)
    {
        var engage = () => new EngageBehavior(settings.Engage);
        return new Director(
            module ?? CreateModule(settings),
            settings.Selection,
            settings.Director,
            new TravelBehavior(settings.Movement),
            kind => kind switch
            {
                FateKind.Collect => new CollectBehavior(engage()),
                FateKind.NpcStart => new NpcStartBehavior(),
                FateKind.Escort => new EscortBehavior(engage()),
                _ => engage(),
            },
            w => new BehaviorContext(null, zoneFlightAllowed?.Invoke(w) ?? true, random, landing),
            new CompanionUpkeep(settings.Companion),
            new StrayAggroClear(settings.Engage));
    }
}
