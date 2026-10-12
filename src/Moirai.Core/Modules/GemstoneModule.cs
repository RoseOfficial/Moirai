using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public enum GemstoneStage { Reading, Ranking, Collecting, Mount, FarmingOn }

// The overlay's view of the run: the stage, the zone the character is in with its rank from the
// last read, the currencies, the trip due, and whatever is set aside
public sealed record GemstoneStatus(
    GemstoneStage Stage, ushort Zone, ZoneRank? Rank, int Gemstones, int TuraliVouchers, string? Trip, string? Note);

// Dawntrail gemstones (gemstone design S1-S16): ranks every zone's Shared FATE to the top, buys the
// traders' collectibles whenever the gemstones near the cap, turns the rest into vouchers, buys the
// voucher mount, and farms on. The stage is worked out from the snapshot on every call; the module
// keeps only its read bookkeeping and the zone rotation.
public sealed class GemstoneModule(GemstoneConfig cfg) : IFarmModule
{
    private readonly Dictionary<ushort, int> _completedSinceRead = [];
    private int _lastCompleted;
    private int _readsSeen;
    private int _failuresSeen;
    private long? _readAgainAtEpoch;
    private ZoneRotationModule? _rotation;
    private IReadOnlyList<ushort> _rotationZones = [];
    private readonly HashSet<uint> _learnGivenUp = [];
    private readonly HashSet<uint> _seenInBags = []; // S7/S14: never bought again this session
    private readonly HashSet<uint> _learned = [];    // S7: learned once is learned for the session
    private readonly Dictionary<uint, int> _tripFailuresSeen = [];
    private readonly Dictionary<uint, long> _tripAgainAtEpoch = []; // S12: when a trader set aside is tried again
    private uint? _learning;
    private long? _learnAskEpoch;

    public GemstoneStatus? Status { get; private set; }

    public ModuleDirective Next(WorldSnapshot w, ModuleContext ctx)
    {
        Observe(w, ctx);
        var learn = Learn(w, ctx); // first, so an item set aside this call drops out of the stage and the note
        var landed = w.SharedFate is { Reads: > 0 } ranks ? ranks : null;
        var stage = landed is null ? GemstoneStage.Reading : StageOf(w, landed);
        var gems = w.CountOf(cfg.GemstoneItemId);
        // S13/S17: no trips with shopping off, in the settings or because the exchange window stopped matching
        var trip = landed is null || !cfg.Shopping || !w.ShoppingReady ? null
            : PlanMount(w, stage) ?? (gems >= cfg.SpendAt ? PlanSpend(w, landed, stage, gems) : null);
        Status = new GemstoneStatus(stage, w.TerritoryId, landed?.Of(w.TerritoryId), gems,
            w.CountOf(cfg.TuraliVoucherItemId), trip is null ? null : Describe(trip), Note(w, landed, gems, trip));

        // S5: the ranks are the whole plan, so reads that never land end the run
        if (w.SharedFate is { } read && read.Failures >= cfg.ReadFailuresToStop)
            return new StopSession(StopReason.RanksUnreadable,
                "the Shared FATE window could not be read; /moirai debug with it open shows what it holds");

        if (learn is not null) return learn; // S14

        // S1/S4/S5: the first read before anything else, then the rank-ups the completions predict
        if (ReadDue(landed) && ctx.BetweenFates && (_readAgainAtEpoch is not { } at || w.NowEpoch >= at))
            return new ReadRanks();
        if (landed is null) return new FarmHere();

        // S6/S9/S13: a trip between fates; at the cap with nothing to spend on, the run stops, unless
        // a trader set aside for a while is still to be tried again (S12)
        if (trip is not null)
        {
            if (ctx.BetweenFates) return new GoShopping(trip);
        }
        else if (cfg.Shopping && gems >= cfg.GemstoneCap && !(w.ShoppingReady && cfg.Traders.Any(t => Waiting(w, t.NpcId))))
        {
            return new StopSession(StopReason.GemstonesCapped, CappedSummary(w, gems));
        }

        return Rotate(w, ctx, landed, stage); // S2/S3
    }

    // S4: each completion counts for the zone the character is in when the ledger records it, and a
    // read that lands starts the counts over. S5: a failed read is asked for again after a wait.
    private void Observe(WorldSnapshot w, ModuleContext ctx)
    {
        if (ctx.Completed > _lastCompleted)
            _completedSinceRead[w.TerritoryId] = _completedSinceRead.GetValueOrDefault(w.TerritoryId) + ctx.Completed - _lastCompleted;
        _lastCompleted = ctx.Completed;

        // S7/S14: a collectible once in the bags was bought (or brought) this session; used, it can
        // read unlearned for a moment before the learned check catches up, and must not be bought again
        foreach (var i in cfg.Items)
            if (i.Kind is GemstoneItemKind.Collectible or GemstoneItemKind.Mount && w.CountOf(i.ItemId) > 0)
                _seenInBags.Add(i.ItemId);

        // S7, field report: nothing is ever unlearned, so a check that stops answering for an item (it
        // read two learned riding maps as unlearned in Old Sharlayan) does not put it back on the list
        if (w.LearnedItems is { } learned) _learned.UnionWith(learned);

        // S12: each failed trip sets its trader aside for a while; a trip that works starts its count over
        foreach (var trader in cfg.Traders)
        {
            var failures = Failures(w, trader.NpcId);
            if (failures > _tripFailuresSeen.GetValueOrDefault(trader.NpcId))
                _tripAgainAtEpoch[trader.NpcId] = w.NowEpoch + cfg.TripRetrySeconds;
            _tripFailuresSeen[trader.NpcId] = failures;
        }

        if (w.SharedFate is not { } ranks) return;
        if (ranks.Reads != _readsSeen)
        {
            _readsSeen = ranks.Reads;
            _completedSinceRead.Clear();
            _readAgainAtEpoch = null;
        }
        if (ranks.Failures > _failuresSeen)
            _readAgainAtEpoch = w.NowEpoch + cfg.ReadRetrySeconds;
        _failuresSeen = ranks.Failures;
    }

    // S14: a collectible in the bags that is not learned yet is used in a settled moment, whatever
    // the stage; one that never takes through the grace is set aside for the session (the E8 pattern)
    private ModuleDirective? Learn(WorldSnapshot w, ModuleContext ctx)
    {
        var item = w.LearnedItems is null ? null : cfg.Items.FirstOrDefault(i =>
            i.Kind is GemstoneItemKind.Collectible or GemstoneItemKind.Mount
            && w.CountOf(i.ItemId) > 0 && !Learned(i.ItemId) && !_learnGivenUp.Contains(i.ItemId));
        if (item is null || !ctx.Settled)
        {
            _learnAskEpoch = null;
            return null;
        }
        if (_learning != item.ItemId)
        {
            _learning = item.ItemId;
            _learnAskEpoch = null;
        }
        _learnAskEpoch ??= w.NowEpoch;
        if (w.NowEpoch - _learnAskEpoch < cfg.LearnGraceSeconds)
            return new EnsureLearned(item.ItemId);
        _learnGivenUp.Add(item.ItemId);
        _learnAskEpoch = null;
        return null;
    }

    // S4: the first read; a rank-up the completions since the last read should have brought; or the
    // periodic check against credit the count missed. Never once every zone is at the top.
    private bool ReadDue(SharedFateRanks? landed)
    {
        if (landed is null) return true;
        foreach (var zone in cfg.Zones)
        {
            var rank = landed.Of(zone);
            if (rank is not null && rank.Rank >= cfg.MaxRank) continue;
            var done = _completedSinceRead.GetValueOrDefault(zone);
            if (done == 0) continue;
            if (done >= cfg.ReadEveryCompletions) return true;
            if (rank is not null && done >= rank.RemainingFates()) return true;
        }
        return false;
    }

    // S1: ranking while a zone is below the top; collecting while a collectible can still be had;
    // the mount while it is wanted and can be had; farming on after that
    private GemstoneStage StageOf(WorldSnapshot w, SharedFateRanks ranks)
    {
        if (!AllMaxed(ranks)) return GemstoneStage.Ranking;
        if (cfg.Items.Any(i => i.Kind == GemstoneItemKind.Collectible && StillWanted(w, i))) return GemstoneStage.Collecting;
        if (cfg.BuyMount && MountItem() is { } mount && StillWanted(w, mount)) return GemstoneStage.Mount;
        return GemstoneStage.FarmingOn;
    }

    // S10-S12, S14: an item counts toward its stage while this session can still get it: not learned,
    // its quest done, sold by its trader, the trader not skipped, and not set aside unlearnable
    private bool StillWanted(WorldSnapshot w, GemstoneItem i)
        => !Learned(i.ItemId) && QuestDone(w, i) && !Unavailable(w, i.ItemId)
           && !TraderFailed(w, i.TraderId) && !_learnGivenUp.Contains(i.ItemId);

    // S2/S3: while ranking, the zones nearest the top rank in turn; after that, all of them. The
    // same zones in another order are the same rotation: only a change of zones starts it over.
    private ModuleDirective Rotate(WorldSnapshot w, ModuleContext ctx, SharedFateRanks ranks, GemstoneStage stage)
    {
        IReadOnlyList<ushort> zones = stage == GemstoneStage.Ranking ? RankingZones(ranks) : cfg.Zones;
        if (_rotation is null || zones.Count != _rotationZones.Count || zones.Except(_rotationZones).Any())
        {
            _rotation = new ZoneRotationModule(new RotationConfig
            {
                Zones = zones,
                QuietSeconds = cfg.QuietSeconds,
                MoveTimeoutSeconds = cfg.MoveTimeoutSeconds,
            });
            _rotationZones = zones;
        }
        return _rotation.Next(w, ctx);
    }

    // S2, field report: with every zone below the top rank in the rotation, a night's FATEs spread
    // over all six, none reached rank 4, and the gemstones capped with only riding maps to buy. So
    // the rotation keeps to the few nearest the top rank (by rank, then by progress toward the next;
    // a zone the read did not list last), and a trader's rank-4 stock opens early.
    private List<ushort> RankingZones(SharedFateRanks ranks)
        => cfg.Zones.Where(z => RankOf(ranks, z) < cfg.MaxRank)
            .OrderByDescending(z => RankOf(ranks, z))
            .ThenByDescending(z => ranks.Of(z)?.Progress ?? 0)
            .Take(cfg.RankingZonesAtOnce)
            .ToList();

    // S7: wanted, not in the bags this session, and the trader lists it at the ranks read
    private bool Buyable(WorldSnapshot w, SharedFateRanks ranks, GemstoneItem i)
        => StillWanted(w, i) && !_seenInBags.Contains(i.ItemId) && GatesMet(ranks, i);

    // S8: the dearest collectible the trader would sell now but the gemstones do not cover yet
    private GemstoneItem? SavingFor(WorldSnapshot w, SharedFateRanks ranks)
        => cfg.Items
            .Where(i => i.Kind == GemstoneItemKind.Collectible && i.CurrencyItemId == cfg.GemstoneItemId && Buyable(w, ranks, i))
            .MaxBy(i => i.Cost);

    private bool GatesMet(SharedFateRanks ranks, GemstoneItem i)
        => (i.RequiredRank == 0 || RankOf(ranks, TraderZone(i.TraderId)) >= i.RequiredRank)
           && (!i.RequiresAllZonesMaxed || AllMaxed(ranks));

    // S7/S8: the trader with the most gemstones' worth of buyable collectibles the gemstones cover,
    // every item in catalog order that still fits; with nothing to buy anywhere, vouchers. A trader
    // set aside after a failed trip is passed over until its wait is up (S12).
    private GemstoneTrip? PlanSpend(WorldSnapshot w, SharedFateRanks ranks, GemstoneStage stage, int gems)
    {
        GemstoneTrip? best = null;
        var bestTotal = 0;
        foreach (var trader in HereFirst(w))
        {
            if (Waiting(w, trader.NpcId)) continue;
            var left = gems;
            var slots = FreeSlots(w); // S18: each one bought sits in a bag slot until it is learned
            var buys = new List<GemstonePurchase>();
            foreach (var i in cfg.Items)
            {
                if (i.TraderId != trader.NpcId || i.Kind != GemstoneItemKind.Collectible || i.CurrencyItemId != cfg.GemstoneItemId) continue;
                if (i.Cost > left || slots == 0 || !Buyable(w, ranks, i)) continue;
                buys.Add(new GemstonePurchase(i.ItemId, 1));
                left -= i.Cost;
                slots--;
            }
            if (gems - left > bestTotal)
            {
                best = new GemstoneTrip(trader.NpcId, buys);
                bestTotal = gems - left;
            }
        }
        return best ?? ExchangeTrip(w, ranks, stage, gems);
    }

    // S7: ties go to the trader in the character's zone, then to catalog order
    private IEnumerable<GemstoneTrader> HereFirst(WorldSnapshot w)
        => cfg.Traders.Where(t => t.TerritoryId == w.TerritoryId).Concat(cfg.Traders.Where(t => t.TerritoryId != w.TerritoryId));

    // S8: nothing to buy, so the gemstones are exchanged for something that keeps their worth. Before
    // the mount stage some gemstones are kept for the unlocks still to come, and never less than the
    // dearest one the trader would sell now, or a low threshold would exchange away every step toward
    // it; from the mount stage on every full hundred goes. While a trader set aside for a while has
    // collectibles to sell, nothing is exchanged: its gemstones wait for it (S12).
    private GemstoneTrip? ExchangeTrip(WorldSnapshot w, SharedFateRanks ranks, GemstoneStage stage, int gems)
    {
        if (cfg.Items.Any(i => i.Kind == GemstoneItemKind.Collectible && Waiting(w, i.TraderId) && Buyable(w, ranks, i)))
            return null;
        var keep = stage is GemstoneStage.Mount or GemstoneStage.FarmingOn ? 0
            : Math.Max(cfg.VoucherKeepBeforeMount, SavingFor(w, ranks)?.Cost ?? 0);
        var spendable = gems - keep;
        // S8: while ranking, the next rank 4 opens a trader's whole stock, so only the top of the pile
        // goes, a step at a time; the rest is there for the stock when it opens
        if (stage == GemstoneStage.Ranking) spendable = Math.Min(spendable, cfg.RankingExchangeStep);
        return VoucherOffer(w, ranks) is { } voucher ? Batch(w, voucher, spendable)
            : MaterialOffer(w, ranks, spendable) is { } material ? Batch(w, material, spendable)
            : null;
    }

    // S8: vouchers first: Turali ones where a city trader sells them (the one in this zone first), any
    // other voucher on offer before that, while its stack has room
    private GemstoneItem? VoucherOffer(WorldSnapshot w, SharedFateRanks ranks)
    {
        var offers = cfg.Items
            .Where(i => i.Kind == GemstoneItemKind.Voucher && i.CurrencyItemId == cfg.GemstoneItemId && GatesMet(ranks, i)
                        && !Unavailable(w, i.ItemId) && Open(w, i.TraderId) && Room(w, i) > 0 && Fits(w, i))
            .ToList();
        return offers.Where(i => i.ItemId == cfg.TuraliVoucherItemId)
                   .OrderBy(i => TraderZone(i.TraderId) == w.TerritoryId ? 0 : 1)
                   .FirstOrDefault()
               ?? offers.FirstOrDefault();
    }

    // S8, field report: no voucher shop open (Gadfrid wants rank 3 in every Endwalker zone, the city
    // traders rank 4 in every Dawntrail one), so crafting materials from a zone trader, which the
    // market board takes: the trader in this zone first, and at it the biggest stack held that the
    // exchange fits in, else the material with the most room, so the bags fill a slot at a time
    private GemstoneItem? MaterialOffer(WorldSnapshot w, SharedFateRanks ranks, int spendable)
    {
        foreach (var trader in HereFirst(w))
        {
            var stock = cfg.Items
                .Where(i => i.TraderId == trader.NpcId && i.Kind == GemstoneItemKind.Material && i.CurrencyItemId == cfg.GemstoneItemId
                            && GatesMet(ranks, i) && !Unavailable(w, i.ItemId) && Open(w, i.TraderId) && Room(w, i) > 0 && Fits(w, i))
                .ToList();
            if (stock.Count == 0) continue;
            return stock.Where(i => Room(w, i) >= spendable / i.Cost).OrderByDescending(i => w.CountOf(i.ItemId)).FirstOrDefault()
                   ?? stock.OrderByDescending(i => Room(w, i)).First();
        }
        return null;
    }

    // S8: as many as the gemstones and the stack's room allow, split into exchanges the shop takes
    private GemstoneTrip? Batch(WorldSnapshot w, GemstoneItem item, int spendable)
    {
        var quantity = Math.Min(spendable / item.Cost, Room(w, item));
        if (quantity <= 0) return null;
        var purchases = new List<GemstonePurchase>();
        for (var left = quantity; left > 0; left -= cfg.MaxPerPurchase)
            purchases.Add(new GemstonePurchase(item.ItemId, Math.Min(left, cfg.MaxPerPurchase)));
        return new GemstoneTrip(item.TraderId, purchases);
    }

    private int Room(WorldSnapshot w, GemstoneItem i) => cfg.HoldCap - w.CountOf(i.ItemId);

    // S18: empty bag slots; unknown counts as room
    private static int FreeSlots(WorldSnapshot w) => w.FreeBagSlots ?? int.MaxValue;

    // S18: a stack already held takes more without a slot; a new one needs a slot
    private static bool Fits(WorldSnapshot w, GemstoneItem i) => w.CountOf(i.ItemId) > 0 || FreeSlots(w) > 0;

    // S9: in the mount stage the vouchers buy the mount, whatever the gemstones; one already in the
    // bags is left to learning
    private GemstoneTrip? PlanMount(WorldSnapshot w, GemstoneStage stage)
    {
        if (stage != GemstoneStage.Mount || MountItem() is not { } mount) return null;
        if (w.CountOf(mount.CurrencyItemId) < mount.Cost || _seenInBags.Contains(mount.ItemId)) return null;
        if (Waiting(w, mount.TraderId) || FreeSlots(w) == 0) return null; // S12, S18
        return new GemstoneTrip(mount.TraderId, [new GemstonePurchase(mount.ItemId, 1)]);
    }

    private string Describe(GemstoneTrip trip)
    {
        var trader = cfg.Traders.FirstOrDefault(t => t.NpcId == trip.TraderId)?.Name ?? trip.TraderId.ToString();
        var what = trip.Purchases.GroupBy(p => p.ItemId).Select(g =>
        {
            var quantity = g.Sum(p => p.Quantity);
            return (cfg.Items.FirstOrDefault(i => i.ItemId == g.Key)?.Name ?? g.Key.ToString()) + (quantity > 1 ? $" x{quantity}" : "");
        });
        return $"{trader}: {string.Join(", ", what)}";
    }

    // The overlay's note: what is set aside and why, and a threshold that finds nothing to spend on
    private string? Note(WorldSnapshot w, SharedFateRanks? ranks, int gems, GemstoneTrip? trip)
    {
        var notes = new List<string>();
        var quests = Names(cfg.Items.Where(i => i.RequiredQuestId != 0 && !QuestDone(w, i) && !Learned(i.ItemId)));
        if (quests.Count > 0) notes.Add("needs a quest: " + string.Join(", ", quests));
        var unsold = Names(cfg.Items.Where(i => Unavailable(w, i.ItemId) && !Learned(i.ItemId)));
        if (unsold.Count > 0) notes.Add("its trader would not sell: " + string.Join(", ", unsold));
        var locked = cfg.Traders.Where(t => cfg.LockedTraders.Contains(t.NpcId)).Select(t => t.Name).ToList();
        if (locked.Count > 0) notes.Add("locked for this character: " + string.Join(", ", locked));
        var failed = cfg.Traders.Where(t => !cfg.LockedTraders.Contains(t.NpcId) && TraderFailed(w, t.NpcId)).Select(t => t.Name).ToList();
        if (failed.Count > 0) notes.Add("trip failed, skipped this session: " + string.Join(", ", failed));
        var waiting = cfg.Traders.Where(t => Waiting(w, t.NpcId)).Select(t => t.Name).ToList();
        if (waiting.Count > 0) notes.Add("trip failed, trying again later: " + string.Join(", ", waiting));
        var unlearnable = Names(cfg.Items.Where(i => _learnGivenUp.Contains(i.ItemId)));
        if (unlearnable.Count > 0) notes.Add("could not learn: " + string.Join(", ", unlearnable));
        if (!cfg.Shopping && gems >= cfg.SpendAt) notes.Add($"shopping is off; gemstones {gems}/{cfg.GemstoneCap}");
        else if (cfg.Shopping && !w.ShoppingReady)
            notes.Add("the exchange window does not match what this version knows; shopping is off for this session");
        else if (cfg.Shopping && ranks is not null && gems >= cfg.SpendAt && trip is null && waiting.Count == 0)
            notes.Add(FreeSlots(w) == 0 ? "the bags are full, so nothing new can be bought"
                : SavingFor(w, ranks) is { } target ? $"saving for {target.Name} ({target.Cost})"
                : "nothing to spend the gemstones on");
        return notes.Count == 0 ? null : string.Join("; ", notes);
    }

    // S13: why the cap ends the run
    private string CappedSummary(WorldSnapshot w, int gems)
        => !w.ShoppingReady
            ? $"gemstones at {gems}/{cfg.GemstoneCap}, and the exchange window does not match what this version knows, so nothing can be bought"
            : FreeSlots(w) == 0
                ? $"gemstones at {gems}/{cfg.GemstoneCap} and the bags are full, so nothing new can be bought"
                : $"gemstones at {gems}/{cfg.GemstoneCap} with nothing left to buy or exchange them for";

    private static List<string> Names(IEnumerable<GemstoneItem> items) => items.Select(i => i.Name).Distinct().ToList();

    private ushort TraderZone(uint traderId) => cfg.Traders.FirstOrDefault(t => t.NpcId == traderId)?.TerritoryId ?? 0;

    private bool AllMaxed(SharedFateRanks ranks) => cfg.Zones.All(z => RankOf(ranks, z) >= cfg.MaxRank);

    // A zone the window did not list reads as rank 0: unmaxed, tried in turn, skipped if unreachable
    private static int RankOf(SharedFateRanks ranks, ushort zone) => ranks.Of(zone)?.Rank ?? 0;

    private GemstoneItem? MountItem()
        => cfg.Items.FirstOrDefault(i => i.Kind == GemstoneItemKind.Mount && i.ItemId == cfg.MountItemId);

    private bool Learned(uint itemId) => _learned.Contains(itemId);

    // Unknown quest state counts as done: the trader's window is the authority (S11)
    private static bool QuestDone(WorldSnapshot w, GemstoneItem i)
        => i.RequiredQuestId == 0 || (w.QuestsDone?.Contains(i.RequiredQuestId) ?? true);

    private static bool Unavailable(WorldSnapshot w, uint itemId) => w.UnavailableItems?.Contains(itemId) ?? false;

    private static int Failures(WorldSnapshot w, uint traderId)
        => w.TripFailures is { } failures && failures.TryGetValue(traderId, out var n) ? n : 0;

    // S12: out for the session: no trip can work there, its trips failed too many times in a row, or
    // its shop was found locked in an earlier session
    private bool TraderFailed(WorldSnapshot w, uint traderId)
        => (w.FailedTraders?.Contains(traderId) ?? false) || cfg.LockedTraders.Contains(traderId)
           || Failures(w, traderId) >= cfg.TripFailuresToSkip;

    // S12: set aside after a failed trip, until it is tried again
    private bool Waiting(WorldSnapshot w, uint traderId)
        => !TraderFailed(w, traderId) && Failures(w, traderId) > 0
           && _tripAgainAtEpoch.TryGetValue(traderId, out var at) && w.NowEpoch < at;

    // S8/S12: a trip there can be planned now
    private bool Open(WorldSnapshot w, uint traderId) => !TraderFailed(w, traderId) && !Waiting(w, traderId);
}
