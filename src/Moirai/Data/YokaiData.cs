using Moirai.Core.Modules;

namespace Moirai.Data;

// Curated event knowledge (§8), verified against the game sheets: item and Companion row ids.
// Names are for display only.
//
// - Regular Yo-kai Medals (15167) drop from gold-rated FATEs in the event zones while the
//   Yo-kai Watch (15222, a wrist accessory) is worn.
// - Legendary medals need the matching minion summoned, in that yokai's designated zones; the
//   watch is not required for those. Every minion is purchasable from Nohi at the Gold Saucer.
// - The four 2020 yokai farm expansion zones: Enma and Damona in Stormblood zones, Ananta and
//   Zazel in Heavensward zones.
public static class YokaiData
{
    public const uint WatchItemId = 15222;
    public const uint MedalItemId = 15167;

    // ARR zone ids: 148 Central Shroud, 152 East Shroud, 153 South Shroud, 154 North Shroud,
    // 134 Middle La Noscea, 135 Lower La Noscea, 138 Western La Noscea, 139 Upper La Noscea,
    // 180 Outer La Noscea, 140 Western Thanalan, 141 Central Thanalan, 145 Eastern Thanalan,
    // 146 Southern Thanalan.
    private static readonly ushort[] Heavensward = [397, 398, 399, 400, 401, 402];
    private static readonly ushort[] Stormblood = [612, 613, 614, 620, 621, 622];

    public static IReadOnlyList<Yokai> Roster { get; } =
    [
        new(200, "Jibanyan", 15168, [148, 135, 141], MinionItemId: 15195),
        new(201, "Komasan", 15169, [152, 138, 145], MinionItemId: 15196),
        new(202, "Whisper", 15170, [153, 139, 146], MinionItemId: 15197),
        new(203, "Blizzaria", 15171, [154, 180, 134], MinionItemId: 15198),
        new(204, "Kyubi", 15172, [140, 148, 135], MinionItemId: 15199),
        new(205, "Komajiro", 15173, [141, 152, 138], MinionItemId: 15200),
        new(206, "Manjimutt", 15174, [145, 153, 139], MinionItemId: 15201),
        new(207, "Noko", 15175, [146, 154, 180], MinionItemId: 15202),
        new(208, "Venoct", 15176, [140, 134, 148], MinionItemId: 15203),
        new(209, "Shogunyan", 15177, [152, 135, 141], MinionItemId: 15204),
        new(210, "Hovernyan", 15178, [153, 138, 145], MinionItemId: 15205),
        new(211, "Robonyan F-type", 15179, [146, 139, 154], MinionItemId: 15206),
        new(212, "USApyon", 15180, [180, 134, 140], MinionItemId: 15207),
        new(390, "Lord Enma", 30805, Stormblood, MinionItemId: 30877),
        new(391, "Lord Ananta", 30804, Heavensward, MinionItemId: 30878),
        new(392, "Zazel", 30803, Heavensward, MinionItemId: 30879),
        new(393, "Damona", 30806, Stormblood, MinionItemId: 30880),
    ];

    // Nohi, the medal-exchange vendor at the Gold Saucer: two ENpcResident rows carry the name
    public const ushort GoldSaucerTerritory = 144;
    public static IReadOnlySet<uint> NohiNpcIds { get; } = new HashSet<uint> { 1017247, 1017528 };

    // E9: the exchange window lists the minions in roster order; the Yo-kai tab holds an offset
    public static int ShopIndexOf(uint minionItemId)
    {
        for (var i = 0; i < Roster.Count; i++)
            if (Roster[i].MinionItemId == minionItemId) return i;
        return -1;
    }

    public static Yokai? ByMinion(uint minionId)
    {
        foreach (var y in Roster)
            if (y.MinionId == minionId) return y;
        return null;
    }

    public static IReadOnlyList<uint> MinionIds { get; } = Roster.Select(y => y.MinionId).ToList();

    // Counted into every snapshot: the legendary medals, the regular medal, and the watch
    public static IReadOnlyList<uint> TrackedItemIds { get; } =
        Roster.Select(y => y.LegendaryMedalItemId).Append(MedalItemId).Append(WatchItemId).ToList();
}
