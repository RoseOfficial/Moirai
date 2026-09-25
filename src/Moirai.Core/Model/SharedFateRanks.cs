namespace Moirai.Core.Model;

// One zone's Shared FATE standing as the game's Shared FATE window reported it: the rank, the FATEs
// done toward the next one, and how many the next one takes (0 at the top rank)
public sealed record ZoneRank(ushort TerritoryId, int Rank, int Progress, int Needed)
{
    public int RemainingFates() => Math.Max(0, Needed - Progress);
}

// The shell reader's results (gemstone design §4): the zones from the last read that landed, how
// many reads have landed this run (the module's cue that a new one is in), and the failed attempts
// since the last one that landed
public sealed record SharedFateRanks(IReadOnlyList<ZoneRank> Zones, int Reads, int Failures)
{
    public ZoneRank? Of(ushort territoryId)
    {
        foreach (var z in Zones)
            if (z.TerritoryId == territoryId) return z;
        return null;
    }
}
