using Moirai.Core.Model;

namespace Moirai.Tests;

public class ModelTests
{
    [Fact] // A8: unopened NPC fates assume a 900 s budget
    public void A8_effective_time_defaults_to_900_when_unopened()
    {
        var f = TestData.Fate(startTimeEpoch: 0, timeRemaining: 0);
        Assert.Equal(900, f.EffectiveTimeLeft);
    }

    [Fact]
    public void Effective_time_uses_remaining_when_opened()
    {
        var f = TestData.Fate(startTimeEpoch: 1_000, timeRemaining: 321);
        Assert.Equal(321, f.EffectiveTimeLeft);
    }

    [Fact]
    public void World_item_count_is_zero_when_absent()
    {
        var w = TestData.World();
        Assert.Equal(0, w.CountOf(12345));
    }

    [Fact] // A8: a fate still preparing is unopened even when its start-time field is set
    public void A8_preparing_fate_counts_as_unopened()
    {
        var f = TestData.Fate(phase: FatePhase.Preparing, startTimeEpoch: 5_000, timeRemaining: 0);
        Assert.True(f.IsUnopened);
        Assert.Equal(900, f.EffectiveTimeLeft);
    }

    [Fact] // S4, field report: the Shared FATE agent names every zone of every tab before any rank has arrived, so rank 0 means not yet
    public void S4_a_zone_the_agent_carries_at_rank_zero_has_not_arrived()
    {
        AgentZone[] agent = [new(813, 0, 0), new(956, 0, 0), new(1187, 3, 21), new(1188, 0, 0), new(1189, 2, 5)];
        var read = RankRead.Arrived(agent, [1187, 1188, 1189], (_, rank) => rank == 3 ? 40 : 20);
        Assert.Equal([new ZoneRank(1187, 3, 21, 40), new ZoneRank(1189, 2, 5, 20)], read);
    }

    [Fact] // S4: the read comes back in the listed order whatever order the agent carries the zones in, and leaves other tabs' zones out
    public void S4_a_read_is_the_listed_zones_in_list_order()
    {
        AgentZone[] agent = [new(1189, 1, 2), new(957, 3, 0), new(1187, 4, 0)];
        var read = RankRead.Arrived(agent, [1187, 1188, 1189], (_, rank) => rank == 1 ? 6 : 0);
        Assert.Equal([new ZoneRank(1187, 4, 0, 0), new ZoneRank(1189, 1, 2, 6)], read);
    }
}
