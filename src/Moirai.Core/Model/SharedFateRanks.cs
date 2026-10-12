namespace Moirai.Core.Model;

// One zone's Shared FATE standing as the game's Shared FATE window reported it: the rank, the FATEs
// done toward the next one, and how many the next one takes (0 at the top rank)
public sealed record ZoneRank(ushort TerritoryId, int Rank, int Progress, int Needed)
{
    public int RemainingFates() => Math.Max(0, Needed - Progress);
}

// One zone as the Shared FATE window's agent carries it, before it is known to hold a rank
public readonly record struct AgentZone(ushort TerritoryId, int Rank, int Progress);

public static class RankRead
{
    // The listed zones whose ranks have arrived, in list order, each with the FATEs its next rank
    // takes. The agent names every zone of every tab from the start and fills the ranks in after the
    // window opens (a field report: the other expansions' zones sat there at rank 0 while Dawntrail's
    // were in). A zone the character can enter is rank 1 at least, so rank 0 is one not arrived yet:
    // it is left out, the reader waits for it, and the planner never takes it for a rank (S4).
    public static List<ZoneRank> Arrived(IEnumerable<AgentZone> agent, IReadOnlyList<ushort> zones, Func<ushort, int, int> fatesForNextRank)
    {
        var found = new Dictionary<ushort, ZoneRank>();
        foreach (var z in agent)
        {
            if (z.Rank <= 0 || !zones.Contains(z.TerritoryId)) continue;
            found[z.TerritoryId] = new ZoneRank(z.TerritoryId, z.Rank, z.Progress, fatesForNextRank(z.TerritoryId, z.Rank));
        }
        return zones.Where(found.ContainsKey).Select(t => found[t]).ToList();
    }
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
