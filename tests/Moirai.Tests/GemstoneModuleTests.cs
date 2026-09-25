using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;

namespace Moirai.Tests;

public class GemstoneModuleTests
{
    private const ushort U = 1187, K = 1188, City = 1185, OtherCity = 1186;
    private const uint Gems = 26807, EwVoucher = 35833, TuraliVoucher = 43961, Whistle = 43590;
    private const uint Tepli = 1, Kunuhali = 2, KajeelJa = 3, Beryl = 4, Gadfrid = 5, Edelina = 6;
    private const uint MapU = 43607, RollU = 44312, MinionU = 43571, MapK = 43608, RollK = 44313, CardK = 43873, RollCity = 44310;
    private const uint CardQuest = 70478;

    // Two zones, their traders, a city trader, and the voucher and mount traders: small enough to
    // reason about, shaped like the real catalog (a card sits at Kunuhali here, for the quest gate)
    private static GemstoneConfig Cfg(bool shopping = true, bool buyMount = true, int spendAt = 1400) => new()
    {
        Enabled = true,
        Zones = [U, K],
        Traders =
        [
            new(Tepli, "Tepli", U), new(Kunuhali, "Kunuhali", K), new(KajeelJa, "Kajeel Ja", City),
            new(Beryl, "Beryl", OtherCity), new(Gadfrid, "Gadfrid", 962), new(Edelina, "Edelina", 156),
        ],
        Items =
        [
            new(MapU, "Urqopacha Riding Map", Tepli, 90, Gems, GemstoneItemKind.Collectible, RequiredRank: 2),
            new(RollU, "Windswept Echoes", Tepli, 450, Gems, GemstoneItemKind.Collectible, RequiredRank: 4),
            new(MinionU, "Alpaca Cria", Tepli, 700, Gems, GemstoneItemKind.Collectible, RequiredRank: 4),
            new(MapK, "Kozama'uka Riding Map", Kunuhali, 90, Gems, GemstoneItemKind.Collectible, RequiredRank: 2),
            new(RollK, "Of Glittering Wings", Kunuhali, 450, Gems, GemstoneItemKind.Collectible, RequiredRank: 4),
            new(CardK, "Otis Card", Kunuhali, 300, Gems, GemstoneItemKind.Collectible, RequiredRank: 4, RequiredQuestId: CardQuest),
            new(RollCity, "Morrow's Might", KajeelJa, 450, Gems, GemstoneItemKind.Collectible, RequiresAllZonesMaxed: true),
            new(TuraliVoucher, "Turali Voucher", KajeelJa, 100, Gems, GemstoneItemKind.Voucher, RequiresAllZonesMaxed: true),
            new(TuraliVoucher, "Turali Voucher", Beryl, 100, Gems, GemstoneItemKind.Voucher, RequiresAllZonesMaxed: true),
            new(EwVoucher, "Bicolor Voucher", Gadfrid, 100, Gems, GemstoneItemKind.Voucher),
            new(Whistle, "Ty'aitya Whistle", Edelina, 500, TuraliVoucher, GemstoneItemKind.Mount),
        ],
        GemstoneItemId = Gems,
        MountItemId = Whistle,
        TuraliVoucherItemId = TuraliVoucher,
        Shopping = shopping,
        BuyMount = buyMount,
        SpendAt = spendAt,
    };

    private static readonly uint[] Collectibles = [MapU, RollU, MinionU, MapK, RollK, CardK, RollCity];

    private static int Step(int rank) => rank switch { 1 => 6, 2 => 20, 3 => 40, _ => 0 };

    private static SharedFateRanks Ranks(int u = 4, int k = 4, int progressU = 0, int progressK = 0, int reads = 1, int failures = 0)
        => new([new ZoneRank(U, u, progressU, Step(u)), new ZoneRank(K, k, progressK, Step(k))], reads, failures);

    private static WorldSnapshot W(
        ushort territory = U, SharedFateRanks? ranks = null, bool unread = false, int gems = 0,
        Dictionary<uint, int>? items = null, IEnumerable<uint>? learned = null, IEnumerable<uint>? quests = null,
        IEnumerable<uint>? failed = null, IEnumerable<uint>? unavailable = null, long now = 1000)
    {
        var counts = new Dictionary<uint, int>(items ?? new Dictionary<uint, int>()) { [Gems] = gems };
        return TestData.World(now: now, territory: territory, items: counts,
            sharedFate: unread ? null : ranks ?? Ranks(),
            learnedItems: (learned ?? Array.Empty<uint>()).ToHashSet(),
            questsDone: (quests ?? new[] { CardQuest }).ToHashSet(),
            failedTraders: (failed ?? Array.Empty<uint>()).ToHashSet(),
            unavailableItems: (unavailable ?? Array.Empty<uint>()).ToHashSet());
    }

    private static ModuleContext Between(int completed = 0, long idle = 0) => new(idle, Settled: true, Completed: completed, BetweenFates: true);
    private static ModuleContext InFate(int completed = 0) => new(0, Settled: false, Completed: completed, BetweenFates: false);

    [Fact] // S1: nothing but the first read is asked for before one lands, gemstones over the threshold or not
    public void S1_the_first_read_comes_before_anything_else()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<ReadRanks>(m.Next(W(unread: true, gems: 1500), Between()));
        Assert.Equal(GemstoneStage.Reading, m.Status!.Stage);
        Assert.IsType<FarmHere>(m.Next(W(unread: true, gems: 1500), InFate())); // mid-fate the read waits
    }

    [Fact] // S1: a zone below the top rank means ranking
    public void S1_stage_is_ranking_while_a_zone_is_below_the_top()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(ranks: Ranks(u: 4, k: 3)), Between());
        Assert.Equal(GemstoneStage.Ranking, m.Status!.Stage);
    }

    [Fact] // S1: every zone at the top and a collectible not learned means collecting
    public void S1_stage_is_collecting_once_ranked_with_collectibles_left()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(learned: [MapU]), Between());
        Assert.Equal(GemstoneStage.Collecting, m.Status!.Stage);
    }

    [Fact] // S1: everything learned and the whistle wanted is the mount stage; the whistle learned, farming on
    public void S1_stage_moves_to_mount_then_farming_on()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(learned: Collectibles), Between());
        Assert.Equal(GemstoneStage.Mount, m.Status!.Stage);
        m.Next(W(learned: [.. Collectibles, Whistle]), Between());
        Assert.Equal(GemstoneStage.FarmingOn, m.Status!.Stage);
    }

    [Fact] // S1: farming on never ends the run
    public void S1_farming_on_farms_and_never_stops()
        => Assert.IsType<FarmHere>(new GemstoneModule(Cfg()).Next(W(learned: [.. Collectibles, Whistle], gems: 50), Between()));

    [Fact] // S9: with the whistle switch off the mount stage is skipped
    public void S9_whistle_off_skips_the_mount_stage()
    {
        var m = new GemstoneModule(Cfg(buyMount: false));
        m.Next(W(learned: Collectibles), Between());
        Assert.Equal(GemstoneStage.FarmingOn, m.Status!.Stage);
    }

    [Fact] // S2: ranking farms the current zone while it is below the top rank
    public void S2_ranking_farms_the_current_unmaxed_zone()
        => Assert.IsType<FarmHere>(new GemstoneModule(Cfg()).Next(W(territory: U, ranks: Ranks(u: 3, k: 2)), Between()));

    [Fact] // S2: a zone read at the top rank is left for the next one below it
    public void S2_a_maxed_zone_is_left_for_the_next_unmaxed()
    {
        var step = new GemstoneModule(Cfg()).Next(W(territory: U, ranks: Ranks(u: 4, k: 2)), Between());
        Assert.Equal(K, Assert.IsType<MoveToTerritory>(step).TerritoryId);
    }

    [Fact] // S2: a read that shows the rank-up moves the run on
    public void S2_moves_on_when_a_read_shows_the_zone_maxed()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(territory: U, ranks: Ranks(u: 3, k: 2, reads: 1)), Between()));
        var step = m.Next(W(territory: U, ranks: Ranks(u: 4, k: 2, reads: 2)), Between());
        Assert.Equal(K, Assert.IsType<MoveToTerritory>(step).TerritoryId);
    }

    [Fact] // S2: a quiet unmaxed zone is left for the next unmaxed one (G2)
    public void S2_quiet_moves_among_the_unmaxed_zones()
    {
        var step = new GemstoneModule(Cfg()).Next(W(territory: U, ranks: Ranks(u: 3, k: 2)), Between(idle: 120));
        Assert.Equal(K, Assert.IsType<MoveToTerritory>(step).TerritoryId);
    }

    [Fact] // S2: when no unmaxed zone can be reached the run stops (G4/G5)
    public void S2_unreachable_unmaxed_zones_stop_the_run()
    {
        var m = new GemstoneModule(Cfg());
        var ranks = Ranks(u: 4, k: 2);
        Assert.IsType<MoveToTerritory>(m.Next(W(territory: City, ranks: ranks, now: 1000), Between()));
        var stop = m.Next(W(territory: City, ranks: ranks, now: 1060), Between());
        Assert.Equal(StopReason.ZonesUnreachable, Assert.IsType<StopSession>(stop).Reason);
    }

    [Fact] // S2, review focus: a zone the Shared FATE window did not list reads as unmaxed and is still farmed in turn
    public void S2_a_zone_missing_from_the_read_is_unmaxed()
    {
        var onlyU = new SharedFateRanks([new ZoneRank(U, 4, 0, 0)], Reads: 1, Failures: 0);
        var m = new GemstoneModule(Cfg());
        var step = m.Next(W(territory: U, ranks: onlyU), Between());
        Assert.Equal(K, Assert.IsType<MoveToTerritory>(step).TerritoryId);
        Assert.Equal(GemstoneStage.Ranking, m.Status!.Stage);
    }

    [Fact] // S3: once every zone is at the top all of them are farmed in turn
    public void S3_after_ranking_all_zones_rotate()
    {
        var step = new GemstoneModule(Cfg()).Next(W(territory: U, learned: Collectibles), Between(idle: 120));
        Assert.Equal(K, Assert.IsType<MoveToTerritory>(step).TerritoryId);
    }

    [Fact] // S4: completions that should have brought a rank-up ask for a read
    public void S4_reads_when_completions_reach_the_rank_up()
    {
        var m = new GemstoneModule(Cfg());
        var ranks = Ranks(u: 3, k: 2, progressU: 38);
        Assert.IsType<FarmHere>(m.Next(W(ranks: ranks), Between(completed: 0)));
        Assert.IsType<FarmHere>(m.Next(W(ranks: ranks), Between(completed: 1)));  // 39/40
        Assert.IsType<ReadRanks>(m.Next(W(ranks: ranks), Between(completed: 2))); // 40/40
    }

    [Fact] // S4: every 10th completion in an unmaxed zone is checked whatever the count says
    public void S4_reads_every_tenth_completion()
    {
        var m = new GemstoneModule(Cfg());
        var ranks = Ranks(u: 3, k: 2, progressU: 0);
        m.Next(W(ranks: ranks), Between(completed: 0));
        Assert.IsType<FarmHere>(m.Next(W(ranks: ranks), Between(completed: 9)));
        Assert.IsType<ReadRanks>(m.Next(W(ranks: ranks), Between(completed: 10)));
    }

    [Fact] // S4: a read that lands starts the count over, and the next prediction comes from it
    public void S4_a_landed_read_starts_the_count_over()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(ranks: Ranks(u: 3, k: 2, progressU: 38)), Between(completed: 0));
        Assert.IsType<ReadRanks>(m.Next(W(ranks: Ranks(u: 3, k: 2, progressU: 38)), Between(completed: 2)));
        // the read credits only one of the two: 39/40, so the next completion asks again
        Assert.IsType<FarmHere>(m.Next(W(ranks: Ranks(u: 3, k: 2, progressU: 39, reads: 2)), Between(completed: 2)));
        Assert.IsType<ReadRanks>(m.Next(W(ranks: Ranks(u: 3, k: 2, progressU: 39, reads: 2)), Between(completed: 3)));
    }

    [Fact] // S4: completions count for the zone they happen in
    public void S4_completions_count_for_their_own_zone()
    {
        var m = new GemstoneModule(Cfg());
        var ranks = Ranks(u: 3, k: 3, progressU: 39, progressK: 0);
        m.Next(W(territory: K, ranks: ranks), Between(completed: 0));
        Assert.IsType<FarmHere>(m.Next(W(territory: K, ranks: ranks), Between(completed: 1))); // Kozama'uka 1/40; Urqopacha still 39/40
    }

    [Fact] // S4: never once every zone is at the top
    public void S4_no_reads_once_all_zones_are_maxed()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(learned: Collectibles), Between(completed: 0));
        Assert.IsNotType<ReadRanks>(m.Next(W(learned: Collectibles), Between(completed: 50)));
    }

    [Fact] // S4: a read that falls due mid-fate waits for the fate to end
    public void S4_a_due_read_waits_for_between_fates()
    {
        var m = new GemstoneModule(Cfg());
        var ranks = Ranks(u: 3, k: 2, progressU: 39);
        m.Next(W(ranks: ranks), InFate(completed: 0));
        Assert.IsNotType<ReadRanks>(m.Next(W(ranks: ranks), InFate(completed: 1)));
        Assert.IsType<ReadRanks>(m.Next(W(ranks: ranks), Between(completed: 1)));
    }

    [Fact] // S5: a read that failed is asked for again only after the wait
    public void S5_a_failed_read_is_retried_after_a_minute()
    {
        var m = new GemstoneModule(Cfg());
        var failedOnce = new SharedFateRanks([], Reads: 0, Failures: 1);
        Assert.IsType<ReadRanks>(m.Next(W(unread: true, now: 1000), Between()));
        Assert.IsType<FarmHere>(m.Next(W(ranks: failedOnce, now: 1001), Between()));
        Assert.IsType<FarmHere>(m.Next(W(ranks: failedOnce, now: 1059), Between()));
        Assert.IsType<ReadRanks>(m.Next(W(ranks: failedOnce, now: 1061), Between()));
    }

    [Fact] // S5: three failed reads in a row stop the run
    public void S5_three_failed_reads_stop_the_run()
    {
        var step = new GemstoneModule(Cfg()).Next(W(ranks: new SharedFateRanks([], 0, 3)), Between());
        Assert.Equal(StopReason.RanksUnreadable, Assert.IsType<StopSession>(step).Reason);
    }
}
