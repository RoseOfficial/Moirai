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
    private const uint MatU1 = 44053, MatU2 = 44063, MatK = 44054;

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
            new(MatU1, "Silver Lobo Hide", Tepli, 3, Gems, GemstoneItemKind.Material, RequiredRank: 1),
            new(MatU2, "Alpaca Fillet", Tepli, 3, Gems, GemstoneItemKind.Material, RequiredRank: 1),
            new(MatK, "Hammerhead Crocodile Skin", Kunuhali, 3, Gems, GemstoneItemKind.Material, RequiredRank: 1),
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
        IEnumerable<uint>? failed = null, IEnumerable<uint>? unavailable = null, long now = 1000,
        Dictionary<uint, int>? tripFailures = null, int? freeBagSlots = null, bool shoppingReady = true)
    {
        var counts = new Dictionary<uint, int>(items ?? new Dictionary<uint, int>()) { [Gems] = gems };
        return TestData.World(now: now, territory: territory, items: counts,
            sharedFate: unread ? null : ranks ?? Ranks(),
            learnedItems: (learned ?? Array.Empty<uint>()).ToHashSet(),
            questsDone: (quests ?? new[] { CardQuest }).ToHashSet(),
            failedTraders: (failed ?? Array.Empty<uint>()).ToHashSet(),
            unavailableItems: (unavailable ?? Array.Empty<uint>()).ToHashSet(),
            tripFailures: tripFailures ?? [], freeBagSlots: freeBagSlots, shoppingReady: shoppingReady);
    }

    private static ModuleContext Between(int completed = 0, long idle = 0) => new(idle, Settled: true, Completed: completed, BetweenFates: true);
    private static ModuleContext InFate(int completed = 0) => new(0, Settled: false, Completed: completed, BetweenFates: false);
    private static GemstoneTrip Trip(ModuleDirective step) => Assert.IsType<GoShopping>(step).Trip;

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

    [Fact] // S6: below the threshold nothing is bought; at it, a trip
    public void S6_a_trip_only_at_the_threshold()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(gems: 1399), Between()));
        Assert.IsType<GoShopping>(m.Next(W(gems: 1400), Between()));
    }

    [Fact] // S6: never mid-fate
    public void S6_no_trip_mid_fate()
        => Assert.IsNotType<GoShopping>(new GemstoneModule(Cfg()).Next(W(gems: 1450), InFate()));

    [Fact] // S7: the trader with the most affordable worth; every item in catalog order, riding map first
    public void S7_picks_the_richest_trader_and_buys_in_catalog_order()
    {
        var m = new GemstoneModule(Cfg());
        var trip = Assert.IsType<GoShopping>(m.Next(W(gems: 1400), Between())).Trip;
        Assert.Equal(Tepli, trip.TraderId);
        Assert.Equal([MapU, RollU, MinionU], trip.Purchases.Select(p => p.ItemId));
        Assert.Equal("Tepli: Urqopacha Riding Map, Windswept Echoes, Alpaca Cria", m.Status!.Trip);
    }

    [Fact] // S7: an item that does not fit is skipped and a cheaper one after it still bought; the richer trader wins over the one here
    public void S7_skips_what_does_not_fit_and_the_richer_trader_wins()
    {
        // Tepli: the map fits, then nothing else (90); Kunuhali: the map, not the roll, then the card (390)
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg(spendAt: 500)).Next(
            W(territory: U, learned: [RollCity], gems: 500), Between())).Trip;
        Assert.Equal(Kunuhali, trip.TraderId);
        Assert.Equal([MapK, CardK], trip.Purchases.Select(p => p.ItemId));
    }

    [Fact] // S7: on equal worth the trader in the character's zone goes first
    public void S7_ties_go_to_the_trader_here()
    {
        var ranks = Ranks(u: 2, k: 2); // only the riding maps are buyable: 90 at each zone trader
        var fromU = Assert.IsType<GoShopping>(new GemstoneModule(Cfg(spendAt: 100)).Next(W(territory: U, ranks: ranks, gems: 100), Between())).Trip;
        Assert.Equal(Tepli, fromU.TraderId);
        var fromK = Assert.IsType<GoShopping>(new GemstoneModule(Cfg(spendAt: 100)).Next(W(territory: K, ranks: ranks, gems: 100), Between())).Trip;
        Assert.Equal(Kunuhali, fromK.TraderId);
    }

    [Fact] // S7: learned items and items above the zone's rank are not bought
    public void S7_buyable_means_not_learned_and_rank_met()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(
            W(ranks: Ranks(u: 4, k: 3), learned: [MapU, RollU], gems: 1400), Between())).Trip;
        Assert.Equal(Tepli, trip.TraderId);
        Assert.Equal([MinionU], trip.Purchases.Select(p => p.ItemId)); // Kunuhali's rank-4 stock waits for rank 4 there
    }

    [Fact] // S7: the city traders' stock waits for every zone at the top
    public void S7_city_stock_waits_for_all_zones()
    {
        var zoneStock = new[] { MapU, RollU, MinionU, MapK, RollK, CardK };
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(W(learned: zoneStock, gems: 1400), Between())).Trip;
        Assert.Equal(KajeelJa, trip.TraderId);
        Assert.Equal([RollCity], trip.Purchases.Select(p => p.ItemId));
    }

    [Fact] // S7, review focus: a trip that leaves the gemstones above the threshold is followed by the next trader, not the same list
    public void S7_the_next_trip_goes_to_the_next_trader()
    {
        var m = new GemstoneModule(Cfg());
        Assert.Equal(Tepli, Assert.IsType<GoShopping>(m.Next(W(gems: 1400), Between())).Trip.TraderId);
        var after = m.Next(W(learned: [MapU, RollU, MinionU], gems: 1400), Between());
        Assert.Equal(Kunuhali, Assert.IsType<GoShopping>(after).Trip.TraderId);
    }

    [Fact] // S8: nothing to buy while ranking: Endwalker vouchers from Gadfrid, keeping 500 gemstones
    public void S8_before_all_zones_vouchers_come_from_gadfrid_keeping_500()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(
            W(ranks: Ranks(u: 3, k: 3), learned: [MapU, MapK], gems: 1400), Between())).Trip;
        Assert.Equal(Gadfrid, trip.TraderId);
        Assert.Equal(new GemstonePurchase(EwVoucher, 9), trip.Purchases.Single());
    }

    [Fact] // S8: from the mount stage on every full hundred becomes Turali vouchers
    public void S8_after_ranking_turali_vouchers_take_every_full_hundred()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(W(learned: Collectibles, gems: 1450), Between())).Trip;
        Assert.Equal(KajeelJa, trip.TraderId);
        Assert.Equal(new GemstonePurchase(TuraliVoucher, 14), trip.Purchases.Single());
    }

    [Fact] // S8: the city trader in the character's zone sells the Turali vouchers first
    public void S8_turali_vouchers_from_the_city_trader_here()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(
            W(territory: OtherCity, learned: Collectibles, gems: 1400), Between())).Trip;
        Assert.Equal(Beryl, trip.TraderId);
    }

    [Fact] // S9: in the mount stage 500 Turali vouchers send the trip to Edelina, whatever the gemstones
    public void S9_the_mount_trip_at_500_vouchers()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 500 };
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(W(learned: Collectibles, items: items, gems: 10), Between())).Trip;
        Assert.Equal(Edelina, trip.TraderId);
        Assert.Equal(new GemstonePurchase(Whistle, 1), trip.Purchases.Single());
    }

    [Fact] // S9: with the switch off the vouchers stay
    public void S9_no_mount_trip_with_the_switch_off()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 500 };
        Assert.IsType<FarmHere>(new GemstoneModule(Cfg(buyMount: false)).Next(W(learned: Collectibles, items: items, gems: 10), Between()));
    }

    [Fact] // S10: a quest-gated item is noted, never bought, and never holds the stage back
    public void S10_quest_gated_items_do_not_hold_the_stage()
    {
        var m = new GemstoneModule(Cfg());
        var rest = Collectibles.Where(i => i != CardK).ToArray();
        var step = m.Next(W(learned: rest, quests: [], gems: 1400), Between());
        Assert.Equal(GemstoneStage.Mount, m.Status!.Stage);
        Assert.Equal(TuraliVoucher, Assert.IsType<GoShopping>(step).Trip.Purchases.Single().ItemId);
        Assert.Contains("needs a quest: Otis Card", m.Status.Note);

        m.Next(W(learned: rest, quests: [CardQuest]), Between());
        Assert.Equal(GemstoneStage.Collecting, m.Status!.Stage); // the quest done, the card counts again
    }

    [Fact] // S11: an item its trader did not list is set aside for the session and never holds the stage back
    public void S11_unlisted_items_are_set_aside()
    {
        var m = new GemstoneModule(Cfg());
        var rest = Collectibles.Where(i => i != RollCity).ToArray();
        var step = m.Next(W(learned: rest, unavailable: [RollCity], gems: 1400), Between());
        Assert.Equal(GemstoneStage.Mount, m.Status!.Stage);
        Assert.Equal(TuraliVoucher, Assert.IsType<GoShopping>(step).Trip.Purchases.Single().ItemId);
        Assert.Contains("its trader would not sell: Morrow's Might", m.Status.Note);
    }

    [Fact] // S11: a purchase that looked refused and arrived late is learned, and the note about it goes
    public void S11_an_item_set_aside_and_learned_after_all_is_no_longer_noted()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(learned: Collectibles, unavailable: [RollCity]), Between());
        Assert.DoesNotContain("would not sell", m.Status!.Note ?? "");
    }

    [Fact] // S12: a trader whose trip failed is never planned again, and its stock no longer holds the stage
    public void S12_a_failed_trader_is_skipped()
    {
        var m = new GemstoneModule(Cfg());
        var trip = Assert.IsType<GoShopping>(m.Next(W(failed: [Tepli], gems: 1400), Between())).Trip;
        Assert.Equal(Kunuhali, trip.TraderId);
        m.Next(W(failed: [Tepli], learned: [MapK, RollK, CardK, RollCity]), Between());
        Assert.Equal(GemstoneStage.Mount, m.Status!.Stage);
        Assert.Contains("Tepli", m.Status.Note);
    }

    // Every trader that takes gemstones, failed: nothing can absorb them
    private static readonly uint[] AllGemTraders = [Tepli, Kunuhali, KajeelJa, Beryl, Gadfrid];

    [Fact] // S13: at the cap with nowhere to spend the gemstones (no collectible, voucher or material) the run stops
    public void S13_capped_with_nowhere_to_spend_stops()
    {
        var step = new GemstoneModule(Cfg()).Next(W(learned: Collectibles, failed: AllGemTraders, gems: 1500), Between());
        Assert.Equal(StopReason.GemstonesCapped, Assert.IsType<StopSession>(step).Reason);
    }

    [Fact] // S13: below the cap it farms on with a note
    public void S13_nothing_to_spend_below_the_cap_is_a_note()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(learned: Collectibles, failed: AllGemTraders, gems: 1450), Between()));
        Assert.Contains("nothing to spend", m.Status!.Note);
    }

    [Fact] // S8, field report: with no voucher shop open (Gadfrid locked while ranking), crafting materials take the gemstones, 99 an exchange
    public void S8_with_no_voucher_shop_materials_take_the_gemstones()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(
            W(territory: U, ranks: Ranks(u: 3, k: 3), learned: [MapU, MapK], failed: [Gadfrid], gems: 1400), Between())).Trip;
        Assert.Equal(Tepli, trip.TraderId);
        Assert.All(trip.Purchases, p => Assert.Equal(MatU1, p.ItemId));
        Assert.All(trip.Purchases, p => Assert.InRange(p.Quantity, 1, 99));
        Assert.Equal(300, trip.Purchases.Sum(p => p.Quantity)); // keeps 500 back, 3 gemstones each
    }

    [Fact] // S8: materials fill the biggest stack held that the quantity fits in, else the one with the most room
    public void S8_materials_fill_the_biggest_stack_that_fits()
    {
        var ranking = Ranks(u: 3, k: 3);
        var fits = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(W(territory: U, ranks: ranking, learned: [MapU, MapK],
            failed: [Gadfrid], items: new() { [MatU2] = 500 }, gems: 1400), Between())).Trip;
        Assert.All(fits.Purchases, p => Assert.Equal(MatU2, p.ItemId));

        var nearlyFull = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(W(territory: U, ranks: ranking, learned: [MapU, MapK],
            failed: [Gadfrid], items: new() { [MatU2] = 800 }, gems: 1400), Between())).Trip;
        Assert.All(nearlyFull.Purchases, p => Assert.Equal(MatU1, p.ItemId)); // 199 of room does not take 300
    }

    [Fact] // S12, field report: a trader found locked in an earlier session is never planned again, so the run goes straight to materials
    public void S12_a_trader_remembered_as_locked_is_not_visited()
    {
        var m = new GemstoneModule(new GemstoneConfig
        {
            Enabled = true, Zones = Cfg().Zones, Traders = Cfg().Traders, Items = Cfg().Items,
            GemstoneItemId = Gems, MountItemId = Whistle, TuraliVoucherItemId = TuraliVoucher,
            LockedTraders = new HashSet<uint> { Gadfrid },
        });
        var trip = Assert.IsType<GoShopping>(m.Next(
            W(territory: U, ranks: Ranks(u: 3, k: 3), learned: [MapU, MapK], gems: 1400), Between())).Trip;
        Assert.Equal(Tepli, trip.TraderId);
        Assert.Contains("locked for this character: Gadfrid", m.Status!.Note);
    }

    [Fact] // S8: materials come from the trader in the character's zone
    public void S8_materials_from_the_trader_here()
    {
        var trip = Assert.IsType<GoShopping>(new GemstoneModule(Cfg()).Next(
            W(territory: K, ranks: Ranks(u: 3, k: 3), learned: [MapU, MapK], failed: [Gadfrid], gems: 1400), Between())).Trip;
        Assert.Equal(Kunuhali, trip.TraderId);
        Assert.All(trip.Purchases, p => Assert.Equal(MatK, p.ItemId));
    }

    [Fact] // S13: with shopping off there are no trips, and the cap is only noted
    public void S13_shopping_off_never_shops_and_never_stops()
    {
        var m = new GemstoneModule(Cfg(shopping: false));
        Assert.IsType<FarmHere>(m.Next(W(gems: 1500), Between()));
        Assert.Contains("shopping is off", m.Status!.Note);
    }

    [Fact] // S13, review focus: with the voucher and material stacks full, the cap stops the run rather than losing gemstones
    public void S13_a_full_voucher_stack_stops_at_the_cap()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 999, [EwVoucher] = 999, [MatU1] = 999, [MatU2] = 999, [MatK] = 999 };
        var step = new GemstoneModule(Cfg()).Next(W(learned: [.. Collectibles, Whistle], items: items, gems: 1500), Between());
        Assert.Equal(StopReason.GemstonesCapped, Assert.IsType<StopSession>(step).Reason);
    }

    [Fact] // S14: a collectible in the bags is learned in a settled moment, whatever the stage
    public void S14_learns_what_is_in_the_bags()
    {
        var items = new Dictionary<uint, int> { [MapU] = 1 };
        var step = new GemstoneModule(Cfg()).Next(W(ranks: Ranks(u: 2, k: 2), items: items), Between());
        Assert.Equal(MapU, Assert.IsType<EnsureLearned>(step).ItemId);
    }

    [Fact] // S14: an item in the bags is learned rather than bought again, and the trip plans around it
    public void S14_an_item_in_the_bags_is_not_bought_again()
    {
        var m = new GemstoneModule(Cfg());
        var items = new Dictionary<uint, int> { [RollU] = 1 };
        var learned = new[] { MapU, MapK, RollK, CardK, RollCity }; // only Tepli has anything left
        Assert.IsType<EnsureLearned>(m.Next(W(items: items, learned: learned, gems: 1400), Between()));
        Assert.Equal("Tepli: Alpaca Cria", m.Status!.Trip); // the roll in the bags is not bought again
    }

    [Fact] // S14: not while unsettled, and an item that never takes is set aside after the grace
    public void S14_learning_waits_for_settled_and_gives_up_after_the_grace()
    {
        var m = new GemstoneModule(Cfg());
        var items = new Dictionary<uint, int> { [MapU] = 1 };
        Assert.IsNotType<EnsureLearned>(m.Next(W(items: items, now: 1000), InFate()));
        Assert.IsType<EnsureLearned>(m.Next(W(items: items, now: 1001), Between()));
        Assert.IsType<EnsureLearned>(m.Next(W(items: items, now: 1010), Between()));
        Assert.IsNotType<EnsureLearned>(m.Next(W(items: items, now: 1011), Between()));
        Assert.Contains("could not learn: Urqopacha Riding Map", m.Status!.Note);
    }

    [Fact] // S8, final review: vouchers keep enough back for the dearest item still to buy, so a low threshold never starves it
    public void S8_vouchers_keep_back_enough_for_the_dearest_wanted_item()
    {
        var m = new GemstoneModule(Cfg(spendAt: 500));
        var allButMinion = Collectibles.Where(i => i != MinionU).ToArray();
        Assert.IsType<FarmHere>(m.Next(W(learned: allButMinion, gems: 650), Between())); // no exchange that drops below 700
        Assert.Contains("saving for Alpaca Cria", m.Status!.Note);
        var trip = Assert.IsType<GoShopping>(m.Next(W(learned: allButMinion, gems: 700), Between())).Trip;
        Assert.Equal([MinionU], trip.Purchases.Select(p => p.ItemId));
    }

    [Fact] // S7/S14, final review: an item seen in the bags is never bought again this session, even before the learned check catches up
    public void S14_an_item_used_before_the_learned_check_catches_up_is_not_bought_again()
    {
        var m = new GemstoneModule(Cfg());
        uint[] learned = [MapU, MapK, RollK, CardK, RollCity];
        Assert.IsType<EnsureLearned>(m.Next(W(items: new() { [RollU] = 1 }, learned: learned, gems: 1450), Between()));
        // the roll was used: gone from the bags, but the learned check has not caught up yet
        var trip = Assert.IsType<GoShopping>(m.Next(W(learned: learned, gems: 1450), Between())).Trip;
        Assert.Equal([MinionU], trip.Purchases.Select(p => p.ItemId));
    }

    [Fact] // S7, field report: the learned check read both riding maps as unlearned in Old Sharlayan, and the run went to buy one it had
    public void S7_an_item_seen_learned_stays_learned_when_the_check_reads_it_unlearned()
    {
        var m = new GemstoneModule(Cfg());
        var ranking = Ranks(u: 3, k: 3);
        Assert.Equal(Gadfrid, Assert.IsType<GoShopping>(m.Next(W(ranks: ranking, learned: [MapU, MapK], gems: 1400), Between())).Trip.TraderId);
        // the trip to Gadfrid failed; standing in his city the check answers for neither map
        var trip = Assert.IsType<GoShopping>(m.Next(W(territory: 962, ranks: ranking, learned: [], failed: [Gadfrid], gems: 1400), Between())).Trip;
        Assert.Equal(Tepli, trip.TraderId);
        Assert.All(trip.Purchases, p => Assert.Equal(MatU1, p.ItemId));
    }

    [Fact] // S12: a trader whose trip failed is set aside for a while and the next best goes first; after the wait it is tried again
    public void S12_a_failed_trip_sets_the_trader_aside_and_is_tried_again_after_the_wait()
    {
        var m = new GemstoneModule(Cfg());
        var failures = new Dictionary<uint, int> { [Tepli] = 1 }; // Tepli has the most to sell: 1,240 of the 1,400
        Assert.Equal(Kunuhali, Trip(m.Next(W(tripFailures: failures, gems: 1400, now: 1000), Between())).TraderId);
        Assert.Equal(Kunuhali, Trip(m.Next(W(tripFailures: failures, gems: 1400, now: 1599), Between())).TraderId);
        Assert.Equal(Tepli, Trip(m.Next(W(tripFailures: failures, gems: 1400, now: 1600), Between())).TraderId);
    }

    [Fact] // S12: three failed trips in a row skip the trader for the session, and its stock no longer holds the stage
    public void S12_three_failed_trips_in_a_row_skip_the_trader_for_the_session()
    {
        var m = new GemstoneModule(Cfg());
        var failures = new Dictionary<uint, int> { [Tepli] = 3 };
        Assert.Equal(Kunuhali, Trip(m.Next(W(tripFailures: failures, gems: 1400, now: 1000), Between())).TraderId);
        Assert.Equal(Kunuhali, Trip(m.Next(W(tripFailures: failures, gems: 1400, now: 9000), Between())).TraderId); // no wait brings it back
        m.Next(W(tripFailures: failures, learned: [MapK, RollK, CardK, RollCity], now: 9000), Between());
        Assert.Equal(GemstoneStage.Mount, m.Status!.Stage);
        Assert.Contains("skipped this session: Tepli", m.Status.Note);
    }

    [Fact] // S12/S13: while a trader with collectibles left is set aside, its gemstones are not exchanged away, and the cap does not stop the run
    public void S12_a_trader_set_aside_keeps_the_gemstones_for_its_collectibles()
    {
        var m = new GemstoneModule(Cfg());
        var failures = new Dictionary<uint, int> { [Tepli] = 1 };
        uint[] rest = [MapK, RollK, CardK, RollCity]; // only Tepli has collectibles left
        Assert.IsType<FarmHere>(m.Next(W(tripFailures: failures, learned: rest, gems: 1400, now: 1000), Between()));
        Assert.Equal(GemstoneStage.Collecting, m.Status!.Stage);
        Assert.Contains("trying again later: Tepli", m.Status.Note);
        Assert.IsType<FarmHere>(m.Next(W(tripFailures: failures, learned: rest, gems: 1500, now: 1001), Between()));
        Assert.Equal(Tepli, Trip(m.Next(W(tripFailures: failures, learned: rest, gems: 1500, now: 1600), Between())).TraderId);
    }

    [Fact] // S12: a trip that worked starts the count over, so the next failure there waits again
    public void S12_a_failure_after_a_trip_that_worked_waits_again()
    {
        var m = new GemstoneModule(Cfg());
        m.Next(W(tripFailures: new() { [Tepli] = 1 }, gems: 1400, now: 1000), Between());
        m.Next(W(gems: 1400, now: 2000), Between()); // a trip there worked since: no failures in a row
        var again = m.Next(W(tripFailures: new() { [Tepli] = 1 }, gems: 1400, now: 3000), Between());
        Assert.Equal(Kunuhali, Trip(again).TraderId);
    }

    [Fact] // S12: vouchers come from the other city trader while the one here is set aside
    public void S12_vouchers_come_from_another_trader_while_one_is_set_aside()
    {
        var trip = Trip(new GemstoneModule(Cfg()).Next(
            W(territory: City, learned: Collectibles, tripFailures: new() { [KajeelJa] = 1 }, gems: 1400), Between()));
        Assert.Equal(Beryl, trip.TraderId);
    }

    [Fact] // S12: the mount trip waits out Edelina's wait like any other
    public void S12_the_mount_trip_waits_while_its_trader_is_set_aside()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 500 };
        Assert.IsType<FarmHere>(new GemstoneModule(Cfg()).Next(
            W(learned: Collectibles, items: items, tripFailures: new() { [Edelina] = 1 }, gems: 10), Between()));
    }

    [Fact] // S17: an exchange window that does not match its layout turns shopping off for the session; the cap then stops the run with that reason
    public void S17_a_window_that_does_not_match_turns_shopping_off()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(shoppingReady: false, gems: 1400), Between()));
        Assert.Null(m.Status!.Trip);
        Assert.Contains("shopping is off for this session", m.Status.Note);
        var stop = Assert.IsType<StopSession>(m.Next(W(shoppingReady: false, gems: 1500), Between()));
        Assert.Equal(StopReason.GemstonesCapped, stop.Reason);
        Assert.Contains("exchange window", stop.Summary);
    }

    [Fact] // S17/S13: with shopping off for the session no trader is coming back, so one set aside earlier does not hold the stop
    public void S17_the_cap_stops_the_run_even_while_a_trader_is_set_aside()
    {
        var stop = new GemstoneModule(Cfg()).Next(
            W(shoppingReady: false, tripFailures: new() { [Tepli] = 1 }, gems: 1500), Between());
        Assert.Equal(StopReason.GemstonesCapped, Assert.IsType<StopSession>(stop).Reason);
    }

    [Fact] // S17: the mount trip is a trip like the others
    public void S17_no_mount_trip_while_shopping_is_off_for_the_session()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 500 };
        Assert.IsType<FarmHere>(new GemstoneModule(Cfg()).Next(
            W(shoppingReady: false, learned: Collectibles, items: items, gems: 10), Between()));
    }

    [Fact] // S18: every collectible bought sits in a bag slot until it is learned, so a trip takes no more than the bags have room for
    public void S18_a_trip_buys_no_more_new_items_than_the_bags_have_slots()
    {
        var trip = Trip(new GemstoneModule(Cfg()).Next(W(freeBagSlots: 2, gems: 1400), Between()));
        Assert.Equal(Tepli, trip.TraderId);
        Assert.Equal([MapU, RollU], trip.Purchases.Select(p => p.ItemId)); // the minion would be a third
    }

    [Fact] // S18/S13: with the bags full nothing new is bought, the overlay says why, and the cap stops the run with that reason
    public void S18_full_bags_buy_nothing_new_and_the_cap_says_why()
    {
        var m = new GemstoneModule(Cfg());
        Assert.IsType<FarmHere>(m.Next(W(freeBagSlots: 0, gems: 1400), Between()));
        Assert.Contains("the bags are full", m.Status!.Note);
        var stop = Assert.IsType<StopSession>(m.Next(W(freeBagSlots: 0, gems: 1500), Between()));
        Assert.Equal(StopReason.GemstonesCapped, stop.Reason);
        Assert.Contains("the bags are full", stop.Summary);
    }

    [Fact] // S18: with no free slot the exchange goes into a stack already held, vouchers or materials
    public void S18_with_no_free_slot_the_exchange_fills_a_stack_already_held()
    {
        var vouchers = Trip(new GemstoneModule(Cfg()).Next(
            W(learned: Collectibles, items: new() { [EwVoucher] = 5 }, freeBagSlots: 0, gems: 1450), Between()));
        Assert.Equal(Gadfrid, vouchers.TraderId); // Turali vouchers would start a new stack
        Assert.Equal(new GemstonePurchase(EwVoucher, 14), vouchers.Purchases.Single());

        var materials = Trip(new GemstoneModule(Cfg()).Next(W(territory: U, ranks: Ranks(u: 3, k: 3), learned: [MapU, MapK],
            failed: [Gadfrid], items: new() { [MatU2] = 800 }, freeBagSlots: 0, gems: 1400), Between()));
        Assert.All(materials.Purchases, p => Assert.Equal(MatU2, p.ItemId));
        Assert.Equal(199, materials.Purchases.Sum(p => p.Quantity)); // what the stack held still takes, not a new stack of 300
    }

    [Fact] // S18: the whistle needs a slot too
    public void S18_the_mount_trip_waits_for_a_free_slot()
    {
        var items = new Dictionary<uint, int> { [TuraliVoucher] = 500 };
        Assert.IsType<FarmHere>(new GemstoneModule(Cfg()).Next(
            W(learned: Collectibles, items: items, freeBagSlots: 0, gems: 10), Between()));
    }
}
