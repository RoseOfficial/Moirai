namespace Moirai.Core.Model;

public sealed record GemstonePurchase(uint ItemId, int Quantity);

// One shopping trip the shell's shopper runs from start to finish: a trader, and what to buy there
public sealed record GemstoneTrip(uint TraderId, IReadOnlyList<GemstonePurchase> Purchases);
