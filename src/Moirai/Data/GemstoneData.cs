using System.Numerics;
using Moirai.Core.Modules;

namespace Moirai.Data;

// Where a gemstone trip goes (gemstone design Appendix T): the aetheryte the trip teleports to and
// where the trader stands. Positions come from the Level sheet (type 8 placements); Edelina has
// none, so hers comes from her map coordinates (Mor Dhona 22.2, 4.9, at the Diamond Forge) and its
// height from the mesh on arrival. Each aetheryte is the one nearest the trader by its map marker.
// Edelina alone has a menu before her exchange; the entry comes from the settings.
public sealed record TraderPlace(uint AetheryteId, Vector3 Position, bool HeightUnknown = false, bool HasMenu = false);

public static class GemstoneData
{
    public static IReadOnlyDictionary<uint, TraderPlace> Places { get; } = new Dictionary<uint, TraderPlace>
    {
        [GemstoneCatalog.Tepli] = new(200, new(301.50f, -172.28f, -484.55f)),       // Wachunpelo, ~74 y
        [GemstoneCatalog.Kunuhali] = new(202, new(-200.34f, 6.30f, -522.77f)),      // Ok'hanu, ~53 y
        [GemstoneCatalog.RralWuruq] = new(205, new(-381.56f, 23.54f, -436.93f)),    // Iq Br'aax, ~17 y
        [GemstoneCatalog.Mitepe] = new(207, new(357.75f, -1.49f, 469.42f)),         // Hhusatahwi, ~22 y
        [GemstoneCatalog.Toashana] = new(211, new(-258.37f, 30.00f, -594.20f)),     // The Outskirts, ~38 y
        [GemstoneCatalog.ClerkPx0029] = new(213, new(30.32f, 53.20f, 802.79f)),     // Leynode Mnemo, ~31 y
        [GemstoneCatalog.KajeelJa] = new(216, new(-23.90f, -10.00f, 105.34f)),      // Tuliyollal, ~98 y
        [GemstoneCatalog.Beryl] = new(217, new(-198.78f, 0.90f, -4.33f)),           // Solution Nine, ~199 y
        [GemstoneCatalog.Gadfrid] = new(182, new(78.37f, 5.14f, -36.78f)),          // Old Sharlayan, ~86 y
        [GemstoneCatalog.Edelina] = new(24, new(34.5f, 0f, -829.7f), HeightUnknown: true, HasMenu: true), // Revenant's Toll, ~163 y
    };

    // Counted into every snapshot: every catalog item (collectibles, vouchers, the whistle)
    public static IReadOnlyList<uint> TrackedItemIds { get; } =
        GemstoneCatalog.Items.Select(i => i.ItemId).Distinct().ToList();

    // S10: the quests some items need, checked into every snapshot
    public static IReadOnlyList<uint> GateQuestIds { get; } =
        GemstoneCatalog.Items.Where(i => i.RequiredQuestId != 0).Select(i => i.RequiredQuestId).Distinct().ToList();
}
