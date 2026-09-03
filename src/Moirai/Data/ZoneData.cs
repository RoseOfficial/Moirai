namespace Moirai.Data;

// Curated zone knowledge. Interim: baked constants; the versioned-JSON data layer replaces this.
public static class ZoneData
{
    // Zones whose geometry breaks flight pathing: travel on the ground there.
    public static IReadOnlySet<ushort> NoFlyTerritories { get; } = new HashSet<ushort>
    {
        140, // Western Thanalan: giant bridge and elevator geometry
        141, // Central Thanalan: mine shafts
        146, // Southern Thanalan
        180, // Outer La Noscea: low flight ceiling
    };
}
