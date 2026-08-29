using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;

namespace Moirai.Tests;

public class YokaiModuleTests
{
    private static readonly Yokai Jibanyan = new(101, "Jibanyan", 501, [140, 141]);
    private static readonly Yokai Komasan = new(102, "Komasan", 502, [145]);

    private static YokaiModule Module() => new([Jibanyan, Komasan], [101, 102], legendaryCap: 10);

    private static WorldSnapshot W(
        bool watchActive = true, bool watchOwned = true, uint? minion = null,
        ushort territory = 140, Dictionary<uint, int>? items = null)
        => TestData.World(
            territory: territory,
            player: TestData.Player(watchActive: watchActive, watchOwned: watchOwned, minionId: minion),
            items: items ?? new Dictionary<uint, int>());

    [Fact] // E1: watch not active but owned -> equip
    public void E1_equips_watch_when_owned()
        => Assert.IsType<EnsureWatch>(Module().Next(W(watchActive: false)));

    [Fact] // E1: watch not owned -> stop
    public void E1_stops_when_watch_not_owned()
    {
        var stop = Assert.IsType<StopSession>(Module().Next(W(watchActive: false, watchOwned: false)));
        Assert.Equal(StopReason.WatchMissing, stop.Reason);
    }

    [Fact] // E2: first under-cap yokai in priority order is the target
    public void E2_targets_first_uncapped_in_priority_order()
    {
        var items = new Dictionary<uint, int> { [501] = 10, [502] = 3 };
        var directive = Assert.IsType<EnsureMinion>(Module().Next(W(minion: 101, items: items)));
        Assert.Equal(102u, directive.MinionId);
    }

    [Fact] // E3: no minion out (dismissed on a transition) -> resummon
    public void E3_resummons_missing_minion()
        => Assert.Equal(101u, Assert.IsType<EnsureMinion>(Module().Next(W(minion: null))).MinionId);

    [Fact] // E4: wrong zone -> move to the yokai's first designated territory
    public void E4_moves_to_designated_zone()
        => Assert.Equal(140, Assert.IsType<MoveToTerritory>(Module().Next(W(minion: 101, territory: 999))).TerritoryId);

    [Fact] // E4: in a designated zone with the right minion -> farm
    public void Farms_when_everything_lines_up()
        => Assert.IsType<FarmHere>(Module().Next(W(minion: 101, territory: 141)));

    [Fact] // E5: all capped -> stop with a per-yokai summary
    public void E5_stops_with_summary_when_all_capped()
    {
        var items = new Dictionary<uint, int> { [501] = 10, [502] = 10 };
        var stop = Assert.IsType<StopSession>(Module().Next(W(minion: 101, items: items)));
        Assert.Equal(StopReason.AllYokaiCapped, stop.Reason);
        Assert.Contains("Jibanyan: 10", stop.Summary);
        Assert.Contains("Komasan: 10", stop.Summary);
    }

    [Fact] // E6: counts come from the snapshot every call — no caching
    public void E6_reflects_fresh_counts_each_call()
    {
        var m = Module();
        Assert.IsType<FarmHere>(m.Next(W(minion: 101, items: new Dictionary<uint, int> { [501] = 9 })));
        Assert.IsType<EnsureMinion>(m.Next(W(minion: 101, items: new Dictionary<uint, int> { [501] = 10 })));
    }
}
