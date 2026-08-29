using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public sealed class YokaiModule(
    IReadOnlyList<Yokai> roster,
    IReadOnlyList<uint> priority,
    int legendaryCap = 10) : IFarmModule
{
    public ModuleDirective Next(WorldSnapshot w)
    {
        var p = w.Player;

        // E1
        if (!p.YokaiWatchActive)
            return p.YokaiWatchOwned
                ? new EnsureWatch()
                : new StopSession(StopReason.WatchMissing, "Yo-kai Watch not owned");

        // E2/E6: fresh counts, first under-cap in priority order
        var target = InPriorityOrder()
            .FirstOrDefault(y => w.CountOf(y.LegendaryMedalItemId) < legendaryCap);
        if (target is null)
            return new StopSession(StopReason.AllYokaiCapped, Summary(w)); // E5

        // E3
        if (p.ActiveMinionId != target.MinionId)
            return new EnsureMinion(target.MinionId);

        // E4: legendary medals only drop in designated zones
        if (!target.TerritoryIds.Contains(w.TerritoryId))
            return new MoveToTerritory(target.TerritoryIds[0]);

        return new FarmHere();
    }

    private IEnumerable<Yokai> InPriorityOrder()
        => priority.Select(id => roster.First(y => y.MinionId == id));

    private string Summary(WorldSnapshot w)
        => string.Join(", ", InPriorityOrder().Select(y => $"{y.Name}: {w.CountOf(y.LegendaryMedalItemId)}"));
}
