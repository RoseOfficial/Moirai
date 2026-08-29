using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public sealed class YokaiModule(
    IReadOnlyList<Yokai> roster,
    IReadOnlyList<uint> priority,
    int legendaryCap = 10,
    uint medalItemId = 0,
    bool autoBuy = false) : IFarmModule
{
    public ModuleDirective Next(WorldSnapshot w)
    {
        var p = w.Player;

        // E1
        if (!p.YokaiWatchActive)
            return p.YokaiWatchOwned
                ? new EnsureWatch()
                : new StopSession(StopReason.WatchMissing, "Yo-kai Watch not owned");

        // E2/E6: fresh counts, first under-cap in priority order; unowned yokai are
        // bought when possible, otherwise skipped so the run never stalls
        var missing = new List<Yokai>();
        foreach (var y in InPriorityOrder())
        {
            if (w.CountOf(y.LegendaryMedalItemId) >= legendaryCap) continue;

            if (Owned(w, y))
            {
                // E3
                if (p.ActiveMinionId != y.MinionId)
                    return new EnsureMinion(y.MinionId);
                // E4: legendary medals only drop in designated zones
                if (!y.TerritoryIds.Contains(w.TerritoryId))
                    return new MoveToTerritory(y.TerritoryIds[0]);
                return new FarmHere();
            }

            if (autoBuy && y.MinionItemId != 0 && medalItemId != 0 && w.CountOf(medalItemId) >= PriceFor(w))
                return new BuyMinion(y.MinionId, y.MinionItemId, PriceFor(w));

            missing.Add(y);
        }

        return missing.Count > 0
            ? new StopSession(StopReason.MinionsMissing,
                "Minions not owned: " + string.Join(", ", missing.Select(m => m.Name)))
            : new StopSession(StopReason.AllYokaiCapped, Summary(w)); // E5
    }

    private bool Owned(WorldSnapshot w, Yokai y)
        => w.OwnedMinions?.Contains(y.MinionId) ?? true;

    // The first minion costs 1 regular medal; every later one costs 3
    private static int PriceFor(WorldSnapshot w)
        => w.OwnedMinions is { Count: > 0 } ? 3 : 1;

    private IEnumerable<Yokai> InPriorityOrder()
        => priority.Select(id => roster.First(y => y.MinionId == id));

    private string Summary(WorldSnapshot w)
        => string.Join(", ", InPriorityOrder().Select(y => $"{y.Name}: {w.CountOf(y.LegendaryMedalItemId)}"));
}
