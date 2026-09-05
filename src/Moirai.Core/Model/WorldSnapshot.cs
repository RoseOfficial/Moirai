namespace Moirai.Core.Model;

public sealed record WorldSnapshot(
    long NowEpoch,
    long NowMs, // monotonic milliseconds for windows the planner measures
    ushort TerritoryId,
    PlayerSnapshot Player,
    IReadOnlyList<FateSnapshot> Fates,
    IReadOnlyList<Aetheryte> Aetherytes,
    IReadOnlyList<EnemySnapshot> Enemies,
    IReadOnlyList<InteractableSnapshot> Interactables,
    IReadOnlyDictionary<uint, int> ItemCounts,
    bool NavmeshReady,
    bool LifestreamBusy,
    DialogKind Dialog = DialogKind.None,
    bool CombatReady = true,      // D8: the combat backend is loaded
    bool TextAdvanceReady = true) // D8: TextAdvance is loaded (it drives Talk and hand-in windows)
{
    public int CountOf(uint itemId) => ItemCounts.TryGetValue(itemId, out var n) ? n : 0;

    public FateSnapshot? FateById(uint id)
    {
        foreach (var f in Fates)
            if (f.Id == id) return f;
        return null;
    }
}
