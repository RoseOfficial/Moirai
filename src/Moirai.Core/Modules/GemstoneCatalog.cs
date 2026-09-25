namespace Moirai.Core.Modules;

// The Dawntrail gemstone traders and what they sell, verified against the installed game's sheets
// on 2026-09-25 (FateShop, SpecialShop, FateProgressUI, Achievement 3559-3564; gemstone design
// Appendix T). Rank-4 stock is gated by the zone's "Free Market Friend" achievement, which is rank
// 4 there. Furnishings and materials are not collectibles and are left out. Each zone trader's
// riding map comes first, so it is bought first (S7). Names are for display only.
public static class GemstoneCatalog
{
    public const uint Gemstone = 26807;         // Bicolor Gemstone: a currency, hold 1,500
    public const uint EndwalkerVoucher = 35833; // Bicolor Gemstone Voucher
    public const uint TuraliVoucher = 43961;    // Turali Bicolor Gemstone Voucher
    public const uint TyaityaWhistle = 43590;   // Mount row 342

    public const ushort Urqopacha = 1187, Kozamauka = 1188, YakTel = 1189,
        Shaaloani = 1190, HeritageFound = 1191, LivingMemory = 1192;

    public static IReadOnlyList<ushort> Zones { get; } =
        [Urqopacha, Kozamauka, YakTel, Shaaloani, HeritageFound, LivingMemory];

    public const uint Tepli = 1048628, Kunuhali = 1048778, RralWuruq = 1048933, Mitepe = 1049283,
        Toashana = 1049438, ClerkPx0029 = 1049528, KajeelJa = 1048383, Beryl = 1049082,
        Gadfrid = 1037055, Edelina = 1036913;

    public static IReadOnlyList<GemstoneTrader> Traders { get; } =
    [
        new(Tepli, "Tepli", Urqopacha),
        new(Kunuhali, "Kunuhali", Kozamauka),
        new(RralWuruq, "Rral Wuruq", YakTel),
        new(Mitepe, "Mitepe", Shaaloani),
        new(Toashana, "Toashana", HeritageFound),
        new(ClerkPx0029, "Clerk PX-0029", LivingMemory),
        new(KajeelJa, "Kajeel Ja", 1185),  // Tuliyollal
        new(Beryl, "Beryl", 1186),         // Solution Nine
        new(Gadfrid, "Gadfrid", 962),      // Old Sharlayan
        new(Edelina, "Edelina", 156),      // Mor Dhona
    ];

    public static IReadOnlyList<GemstoneItem> Items { get; } =
    [
        Map(43607, "Urqopacha Riding Map", Tepli),
        Rank4(44312, "Windswept Echoes Orchestrion Roll", Tepli, 450),
        Rank4(45009, "Windswept Whispers Orchestrion Roll", Tepli, 450),
        Rank4(43571, "Alpaca Cria", Tepli, 700),

        Map(43608, "Kozama'uka Riding Map", Kunuhali),
        Rank4(44313, "Of Glittering Wings Orchestrion Roll", Kunuhali, 450),
        Rank4(45010, "Of Glittering Baubles Orchestrion Roll", Kunuhali, 450),

        Map(43609, "Yak T'el Riding Map", RralWuruq),
        Rank4(44314, "Emerald under Sapphire Orchestrion Roll", RralWuruq, 450),
        Rank4(45011, "Skybound Emerald Orchestrion Roll", RralWuruq, 450),
        Rank4(43574, "Ilyikty'i", RralWuruq, 700),

        Map(43610, "Shaaloani Riding Map", Mitepe),
        Rank4(44315, "Ceruleum Smoke at High Noon Orchestrion Roll", Mitepe, 450),
        Rank4(45012, "Ceruleum Embers by Dusk Orchestrion Roll", Mitepe, 450),
        Rank4(43601, "Turali Barding", Mitepe, 450),
        Rank4(44479, "Ttokrrone Card", Mitepe, 300, quest: 70495),

        Map(43611, "Heritage Found Riding Map", Toashana),
        Rank4(44316, "Flash in the Dark Orchestrion Roll", Toashana, 450),
        Rank4(45013, "Crash in the Dark Orchestrion Roll", Toashana, 450),
        Rank4(43873, "Otis Card", Toashana, 300, quest: 70478),

        Map(43612, "Living Memory Riding Map", ClerkPx0029),
        Rank4(44317, "Bygone Serenity Orchestrion Roll", ClerkPx0029, 450),
        Rank4(45014, "Echoes of Memory Orchestrion Roll", ClerkPx0029, 450, quest: 70494),
        Rank4(43874, "Cahciua Card", ClerkPx0029, 300, quest: 70494),
        Rank4(44480, "Mica the Magical Mu Card", ClerkPx0029, 300, quest: 70495),

        City(44310, "Morrow's Might Orchestrion Roll", KajeelJa, 450),
        City(45008, "Morrow's Magic Orchestrion Roll", KajeelJa, 450),
        City(44293, "Crimson Dawn Framer's Kit", KajeelJa, 600),
        City(44294, "Golden Dawn Framer's Kit", KajeelJa, 600),
        new(TuraliVoucher, "Turali Bicolor Gemstone Voucher", KajeelJa, 100, Gemstone, GemstoneItemKind.Voucher, RequiresAllZonesMaxed: true),

        City(44311, "Starless Skyline Orchestrion Roll", Beryl, 450),
        City(43960, "Dark Solution Framer's Kit", Beryl, 600),
        City(44292, "Bright Solution Framer's Kit", Beryl, 600),
        City(44256, "Neon Parasol", Beryl, 500),
        new(TuraliVoucher, "Turali Bicolor Gemstone Voucher", Beryl, 100, Gemstone, GemstoneItemKind.Voucher, RequiresAllZonesMaxed: true),

        new(EndwalkerVoucher, "Bicolor Gemstone Voucher", Gadfrid, 100, Gemstone, GemstoneItemKind.Voucher),
        new(TyaityaWhistle, "Ty'aitya Whistle", Edelina, 500, TuraliVoucher, GemstoneItemKind.Mount),
    ];

    private static GemstoneItem Map(uint id, string name, uint trader)
        => new(id, name, trader, 90, Gemstone, GemstoneItemKind.Collectible, RequiredRank: 2);

    private static GemstoneItem Rank4(uint id, string name, uint trader, int cost, uint quest = 0)
        => new(id, name, trader, cost, Gemstone, GemstoneItemKind.Collectible, RequiredRank: 4, RequiredQuestId: quest);

    private static GemstoneItem City(uint id, string name, uint trader, int cost)
        => new(id, name, trader, cost, Gemstone, GemstoneItemKind.Collectible, RequiresAllZonesMaxed: true);
}
