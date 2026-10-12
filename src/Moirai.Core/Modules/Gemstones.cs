namespace Moirai.Core.Modules;

public enum GemstoneItemKind { Collectible, Voucher, Mount, Material }

// A trader by ENpc id (the ENpcResident and ENpcBase ids are the same); the name is for display only
public sealed record GemstoneTrader(uint NpcId, string Name, ushort TerritoryId);

// One thing a trader sells and what gates it (gemstone design §1, Appendix T). RequiredRank is the
// Shared FATE rank the trader's own zone must have; RequiresAllZonesMaxed is the city traders' gate.
// The name is for display only.
public sealed record GemstoneItem(
    uint ItemId,
    string Name,
    uint TraderId,
    int Cost,
    uint CurrencyItemId,
    GemstoneItemKind Kind,
    int RequiredRank = 0,
    bool RequiresAllZonesMaxed = false,
    uint RequiredQuestId = 0);

public sealed class GemstoneConfig
{
    public bool Enabled { get; init; }
    public IReadOnlyList<ushort> Zones { get; init; } = [];           // farmed in this order (map order)
    public IReadOnlyList<GemstoneTrader> Traders { get; init; } = []; // catalog order breaks ties (S7)
    public IReadOnlyList<GemstoneItem> Items { get; init; } = [];     // per trader, its riding map first (S7)
    public int MaxRank { get; init; } = 4;
    public uint GemstoneItemId { get; init; }
    public int GemstoneCap { get; init; } = 1500;
    public int SpendAt { get; init; } = 1400;                         // S6
    public bool Shopping { get; init; } = true;                       // S13: off means ranks only
    public bool BuyMount { get; init; } = true;                       // S9
    public uint MountItemId { get; init; }
    public uint TuraliVoucherItemId { get; init; }                    // S8: preferred once the city traders sell it
    public int VoucherKeepBeforeMount { get; init; } = 500;           // S8
    public int HoldCap { get; init; } = 999;                          // a voucher or material stack's limit
    public int MaxPerPurchase { get; init; } = 99;                    // S8: the most one exchange takes
    public int ReadEveryCompletions { get; init; } = 10;              // S4
    public int ReadRetrySeconds { get; init; } = 60;                  // S5
    public int ReadFailuresToStop { get; init; } = 3;                 // S5
    public int LearnGraceSeconds { get; init; } = 10;                 // S14
    public IReadOnlySet<uint> LockedTraders { get; init; } = new HashSet<uint>(); // S12: shops found locked in an earlier session
    public int TripRetrySeconds { get; init; } = 600;                 // S12: a trader whose trip failed is tried again after this
    public int TripFailuresToSkip { get; init; } = 3;                 // S12: in a row, they skip the trader for the session
    public int QuietSeconds { get; init; } = 120;                     // G2
    public int MoveTimeoutSeconds { get; init; } = 60;                // G4
}
