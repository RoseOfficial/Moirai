namespace Moirai.Core.Model;

public sealed record WorldSnapshot(
    long NowEpoch,
    ushort TerritoryId,
    PlayerSnapshot Player,
    IReadOnlyList<FateSnapshot> Fates,
    IReadOnlyList<Aetheryte> Aetherytes,
    IReadOnlyList<EnemySnapshot> Enemies,
    IReadOnlyList<InteractableSnapshot> Interactables,
    IReadOnlyDictionary<uint, int> ItemCounts,
    bool NavmeshReady,
    bool LifestreamBusy,
    IReadOnlySet<uint>? OwnedMinions = null) // null = ownership unknown (treated as owned)
{
    public int CountOf(uint itemId) => ItemCounts.TryGetValue(itemId, out var n) ? n : 0;

    public FateSnapshot? FateById(uint id)
    {
        foreach (var f in Fates)
            if (f.Id == id) return f;
        return null;
    }
}
