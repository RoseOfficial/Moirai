namespace Moirai.Core.Modules;

// One row of the event table (§8): the minion, its legendary medal, and the zones those medals
// drop in. The name is for display only; every decision keys off the ids.
public sealed record Yokai(
    uint MinionId,
    string Name,
    uint LegendaryMedalItemId,
    IReadOnlyList<ushort> TerritoryIds,
    uint MinionItemId = 0); // the purchasable item that teaches the minion, for the shopping list

public sealed class YokaiConfig
{
    public bool Enabled { get; init; }
    public IReadOnlyList<Yokai> Roster { get; init; } = [];
    public IReadOnlyList<uint> Priority { get; init; } = []; // minion ids; empty means roster order
    public int LegendaryCap { get; init; } = 10;              // E2/E5
    public uint WatchItemId { get; init; }
    public bool AutoEquipWatch { get; init; } = true;         // E1
    public int QuietSeconds { get; init; } = 120;             // E7: rotation within the yokai's zones
    public int MoveTimeoutSeconds { get; init; } = 60;
    public uint MedalItemId { get; init; }                    // E9: the regular medal minions are bought with
    public bool AutoBuy { get; init; }                        // E9: ask the shell to buy the next unowned minion
}
