namespace Moirai.Core.Modules;

public sealed record Yokai(
    uint MinionId,
    string Name,
    uint LegendaryMedalItemId,
    IReadOnlyList<ushort> TerritoryIds,
    uint MinionItemId = 0); // the purchasable item that teaches the minion
