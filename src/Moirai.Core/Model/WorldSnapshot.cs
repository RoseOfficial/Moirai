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
    DialogKind Dialog = DialogKind.None,
    bool CombatReady = true,      // D8: the combat backend is loaded
    bool TextAdvanceReady = true, // D8: TextAdvance is loaded (it drives Talk and hand-in windows)
    bool DodgeReady = false,      // H1: the dodge layer (BossMod Reborn) is loaded
    bool Danger = false,          // H2: the dodge layer is steering us or a marked zone is about to go off
    IReadOnlySet<uint>? OwnedMinions = null, // §8: Companion row ids unlocked; null when unknown
    bool AutoBuyReady = true,                // E9: the shell's purchaser has not failed this run
    bool RepairReady = true,                 // R5: the shell's repairer has not failed this run
    bool DutyPopped = false,                 // D11: the duty finder's ready window is up
    SharedFateRanks? SharedFate = null,      // S4: the Shared FATE reader's results; null until a read is tried
    IReadOnlySet<uint>? LearnedItems = null, // S7/S14: catalog items this character has learned; null when unknown
    IReadOnlySet<uint>? QuestsDone = null,   // S10: the catalog's gate quests that are complete; null when unknown
    IReadOnlySet<uint>? FailedTraders = null,   // S12: traders whose trip failed this session
    IReadOnlySet<uint>? UnavailableItems = null) // S11: items a trader's window did not list this session
{
    public int CountOf(uint itemId) => ItemCounts.TryGetValue(itemId, out var n) ? n : 0;

    public FateSnapshot? FateById(uint id)
    {
        foreach (var f in Fates)
            if (f.Id == id) return f;
        return null;
    }
}
