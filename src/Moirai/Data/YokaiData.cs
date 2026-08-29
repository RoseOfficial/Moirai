using Moirai.Core.Modules;

namespace Moirai.Data;

// Curated game knowledge. Interim: baked constants; the versioned-JSON data layer replaces this.
public static class YokaiData
{
    // Filled from verified game data before first in-game run.
    public static IReadOnlyList<Yokai> Roster { get; } = [];

    public static uint WatchItemId => 0;
    public static uint MedalItemId => 0;

    public static IReadOnlyList<uint> TrackedItemIds { get; } =
        Roster.Select(y => y.LegendaryMedalItemId)
              .Append(WatchItemId)
              .Append(MedalItemId)
              .Where(id => id != 0)
              .ToList();

    // Zones whose geometry breaks flight pathing — travel on the ground there.
    public static IReadOnlySet<ushort> NoFlyTerritories { get; } = new HashSet<ushort>
    {
        140, // Western Thanalan: giant bridge/elevator geometry
        141, // Central Thanalan: mine shafts
        146, // Southern Thanalan
        180, // Outer La Noscea: low flight ceiling
    };
}
