using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;

namespace Moirai.Tests;

public class YokaiModuleTests
{
    private const uint Watch = 15222;
    private static readonly Yokai Jibanyan = new(200, "Jibanyan", 15168, [148, 135, 141]);
    private static readonly Yokai Komasan = new(201, "Komasan", 15169, [152, 138, 145]);

    private static YokaiConfig Cfg(params uint[] priority) => new()
    {
        Enabled = true,
        Roster = [Jibanyan, Komasan],
        Priority = priority.Length == 0 ? [200, 201] : priority,
        LegendaryCap = 10,
        WatchItemId = Watch,
        AutoEquipWatch = true,
        QuietSeconds = 120,
        MoveTimeoutSeconds = 60,
    };

    private static WorldSnapshot W(
        ushort territory = 148, uint? minion = 200, bool watchEquipped = true, bool watchOwned = true,
        IReadOnlySet<uint>? owned = null, Dictionary<uint, int>? items = null, long now = 1000)
        => TestData.World(now: now, territory: territory,
            player: TestData.Player(activeMinionId: minion, watchEquipped: watchEquipped, watchOwned: watchOwned),
            items: items ?? new Dictionary<uint, int>(), ownedMinions: owned);

    private static ModuleContext Settled(long idle = 0) => new(IdleSeconds: idle, Settled: true);
    private static ModuleContext Busy(long idle = 0) => new(IdleSeconds: idle, Settled: false);

    [Fact] // E1: the watch is worn when owned; it is judged by the equipped slot, not the bags
    public void E1_equips_the_watch_when_owned_but_not_worn()
    {
        var step = new YokaiModule(Cfg()).Next(W(watchEquipped: false, watchOwned: true), Settled());
        Assert.IsType<EnsureWatch>(step);
    }

    [Fact] // E1: legendary medals need the minion, not the watch, so a missing watch is a note and farming goes on
    public void E1_missing_watch_is_a_note_not_a_stop()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(watchEquipped: false, watchOwned: false), Settled()));
        Assert.Contains("Watch", m.Status!.Note);
    }

    [Fact] // E2: the first yokai under the cap in priority order is the target
    public void E2_targets_first_under_cap_in_priority_order()
    {
        var items = new Dictionary<uint, int> { [15168] = 10 }; // Jibanyan capped
        var m = new YokaiModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(territory: 152, minion: 201, items: items), Settled()));
        Assert.Equal("Komasan", m.Status!.Name);
        Assert.Equal(0, m.Status.Legendary);
    }

    [Fact] // E3: the active yokai's minion is summoned when it is not out
    public void E3_summons_the_missing_minion()
    {
        var step = new YokaiModule(Cfg()).Next(W(minion: null), Settled());
        Assert.Equal(200u, Assert.IsType<EnsureMinion>(step).MinionId);
    }

    [Fact] // E3: a minion that never appears through the summon grace is given up on for the session
    public void E3_summon_that_never_takes_skips_the_yokai()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<EnsureMinion>(m.Next(W(minion: null, now: 1000), Settled()));
        Assert.IsType<EnsureMinion>(m.Next(W(minion: null, now: 1019), Settled()));
        var next = m.Next(W(minion: null, now: 1021), Settled());
        Assert.Equal(152, Assert.IsType<MoveToTerritory>(next).TerritoryId); // Komasan's zones now
        Assert.Equal("Komasan", m.Status!.Name);
        Assert.Contains("Jibanyan", m.Status.Note);
    }

    [Fact] // E3: a summon is asked for in settled moments only, and a leg in between restarts the grace
    public void E3_summon_waits_for_a_settled_moment_and_travel_restarts_the_grace()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<EnsureMinion>(m.Next(W(minion: null, now: 1000), Settled()));
        Assert.IsType<FarmHere>(m.Next(W(minion: null, now: 1010), Busy()));         // mid-leg: farming goes on
        Assert.IsType<EnsureMinion>(m.Next(W(minion: null, now: 1020), Settled()));  // grace starts over here
        Assert.Equal(200u, Assert.IsType<EnsureMinion>(m.Next(W(minion: null, now: 1039), Settled())).MinionId);
    }

    [Fact] // E4: outside every designated zone, go to the yokai's first one
    public void E4_wrong_zone_moves_to_the_yokais_zone()
    {
        var step = new YokaiModule(Cfg()).Next(W(territory: 129), Settled());
        Assert.Equal(148, Assert.IsType<MoveToTerritory>(step).TerritoryId);
    }

    [Fact] // E7: a quiet designated zone is left for the yokai's next one, as the zone rotation does
    public void E7_quiet_zone_rotates_within_the_yokais_zones()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(territory: 148), Settled(idle: 119)));
        Assert.Equal(135, Assert.IsType<MoveToTerritory>(m.Next(W(territory: 148), Settled(idle: 120))).TerritoryId);
    }

    [Fact] // E7: a new active yokai gets its own rotation
    public void E7_rotation_follows_the_active_yokai()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(territory: 148), Settled()));
        var capped = new Dictionary<uint, int> { [15168] = 10 };
        Assert.Equal(152, Assert.IsType<MoveToTerritory>(m.Next(W(territory: 148, items: capped), Settled())).TerritoryId);
    }

    [Fact] // E5: every yokai capped stops the run with a per-yokai summary
    public void E5_all_capped_stops_with_summary()
    {
        var items = new Dictionary<uint, int> { [15168] = 10, [15169] = 12 };
        var stop = Assert.IsType<StopSession>(new YokaiModule(Cfg()).Next(W(items: items), Settled()));
        Assert.Equal(StopReason.AllYokaiCapped, stop.Reason);
        Assert.Contains("Jibanyan", stop.Summary);
        Assert.Contains("Komasan", stop.Summary);
    }

    [Fact] // E5: nothing farmable because the minions are not owned stops with the shopping list
    public void E5_unowned_minions_stop_with_a_shopping_list()
    {
        var stop = Assert.IsType<StopSession>(new YokaiModule(Cfg()).Next(W(owned: new HashSet<uint>()), Settled()));
        Assert.Equal(StopReason.MinionsMissing, stop.Reason);
        Assert.Contains("Jibanyan", stop.Summary);
    }

    [Fact] // an unowned yokai is skipped for the next owned one
    public void Unowned_yokai_is_skipped_for_the_next_owned()
    {
        var step = new YokaiModule(Cfg()).Next(W(territory: 152, minion: 201, owned: new HashSet<uint> { 201 }), Settled());
        Assert.IsType<FarmHere>(step);
    }

    [Fact] // E6: counts come from the snapshot every call
    public void E6_counts_are_read_fresh_each_call()
    {
        var m = new YokaiModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(), Settled()));
        Assert.Equal("Jibanyan", m.Status!.Name);
        var capped = new Dictionary<uint, int> { [15168] = 10 };
        m.Next(W(items: capped), Settled());
        Assert.Equal("Komasan", m.Status!.Name);
    }
}
