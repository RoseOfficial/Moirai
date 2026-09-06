namespace Moirai.Data;

// Curated zone knowledge. Interim: baked constants; the versioned-JSON data layer replaces this.
public static class ZoneData
{
    // C1: zones to travel on the ground in even where the game allows flight. Empty until a zone
    // earns an entry with evidence from a run: the first cut listed four ARR zones on a guess
    // about their geometry, and every leg there rode on the ground for nothing.
    public static IReadOnlySet<ushort> NoFlyTerritories { get; } = new HashSet<ushort>();
}
