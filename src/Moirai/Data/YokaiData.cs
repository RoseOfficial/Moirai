using Moirai.Core.Modules;

namespace Moirai.Data;

// Curated game knowledge. Interim: baked constants; the versioned-JSON data layer replaces this.
//
// Yo-kai event mechanics (verified against consolegameswiki + garlandtools + xivapi):
// - REGULAR Yo-kai Medals (15167) drop from gold-rated FATEs in any event zone, but only
//   while the Yo-kai Watch (15222, a wrist-slot equip) is worn.
// - LEGENDARY medals require the matching MINION summoned, in that yokai's designated zones;
//   the watch is NOT required for those. All 17 minions purchasable from day one (no weapon gate).
// - Zone split for the 2020 four is not the intuitive pairing: Enma/Damona farm Stormblood
//   zones, Ananta/Zazel farm Heavensward zones (two sources against one on this).
public static class YokaiData
{
    public const uint WatchItemId = 15222;
    public const uint MedalItemId = 15167;

    // ARR zone rotation ids: 148 CShroud, 152 EShroud, 153 SShroud, 154 NShroud,
    // 134 MidLaNo, 135 LowLaNo, 138 WLaNo, 139 UpLaNo, 180 OuterLaNo,
    // 140 WThan, 141 CThan, 145 EThan, 146 SThan.
    private static readonly ushort[] Heavensward = [397, 398, 399, 400, 401, 402];
    private static readonly ushort[] Stormblood = [612, 613, 614, 620, 621, 622];

    public static IReadOnlyList<Yokai> Roster { get; } =
    [
        new(200, "Jibanyan", 15168, [148, 135, 141]),
        new(201, "Komasan", 15169, [152, 138, 145]),
        new(202, "Whisper", 15170, [153, 139, 146]),
        new(203, "Blizzaria", 15171, [154, 180, 134]),
        new(204, "Kyubi", 15172, [140, 148, 135]),
        new(205, "Komajiro", 15173, [141, 152, 138]),
        new(206, "Manjimutt", 15174, [145, 153, 139]),
        new(207, "Noko", 15175, [146, 154, 180]),
        new(208, "Venoct", 15176, [140, 134, 148]),
        new(209, "Shogunyan", 15177, [152, 135, 141]),
        new(210, "Hovernyan", 15178, [153, 138, 145]),
        new(211, "Robonyan F-type", 15179, [146, 139, 154]),
        new(212, "USApyon", 15180, [180, 134, 140]),
        new(390, "Lord Enma", 30805, Stormblood),
        new(391, "Lord Ananta", 30804, Heavensward),
        new(392, "Zazel", 30803, Heavensward),
        new(393, "Damona", 30806, Stormblood),
    ];

    public static IReadOnlyList<uint> TrackedItemIds { get; } =
        Roster.Select(y => y.LegendaryMedalItemId)
              .Append(WatchItemId)
              .Append(MedalItemId)
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
