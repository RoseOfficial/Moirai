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

    public GemstoneStatus? Status { get; private set; }

    public ModuleDirective Next(WorldSnapshot w, ModuleContext ctx)
    {
        Observe(w, ctx);
        var landed = w.SharedFate is { Reads: > 0 } ranks ? ranks : null;
        var stage = landed is null ? GemstoneStage.Reading : StageOf(w, landed);
        var gems = w.CountOf(cfg.GemstoneItemId);
        Status = new GemstoneStatus(stage, w.TerritoryId, landed?.Of(w.TerritoryId), gems,
            w.CountOf(cfg.TuraliVoucherItemId), null, null);

        // S5: the ranks are the whole plan, so reads that never land end the run
        if (w.SharedFate is { } read && read.Failures >= cfg.ReadFailuresToStop)
            return new StopSession(StopReason.RanksUnreadable,
                "the Shared FATE window could not be read; /moirai debug with it open shows what it holds");

        // S1/S4/S5: the first read before anything else, then the rank-ups the completions predict
        if (ReadDue(landed) && ctx.BetweenFates && (_readAgainAtEpoch is not { } at || w.NowEpoch >= at))
            return new ReadRanks();
        if (landed is null) return new FarmHere();

        return Rotate(w, ctx, landed, stage); // S2/S3
    }

    // S4: each completion counts for the zone the character is in when the ledger records it, and a
    // read that lands starts the counts over. S5: a failed read is asked for again after a wait.
    private void Observe(WorldSnapshot w, ModuleContext ctx)
    {
        if (ctx.Completed > _lastCompleted)
            _completedSinceRead[w.TerritoryId] = _completedSinceRead.GetValueOrDefault(w.TerritoryId) + ctx.Completed - _lastCompleted;
        _lastCompleted = ctx.Completed;

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

    // S10-S12: an item counts toward its stage while this session can still get it: not learned, its
    // quest done, listed by its trader, and the trader's trip not failed
    private bool StillWanted(WorldSnapshot w, GemstoneItem i)
        => !Learned(w, i.ItemId) && QuestDone(w, i) && !Unavailable(w, i.ItemId) && !TraderFailed(w, i.TraderId);

    // S2/S3: while ranking, the zones below the top rank in turn; after that, all of them
    private ModuleDirective Rotate(WorldSnapshot w, ModuleContext ctx, SharedFateRanks ranks, GemstoneStage stage)
    {
        IReadOnlyList<ushort> zones = stage == GemstoneStage.Ranking
            ? cfg.Zones.Where(z => RankOf(ranks, z) < cfg.MaxRank).ToList()
            : cfg.Zones;
        if (_rotation is null || !zones.SequenceEqual(_rotationZones))
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

    private bool AllMaxed(SharedFateRanks ranks) => cfg.Zones.All(z => RankOf(ranks, z) >= cfg.MaxRank);

    // A zone the window did not list reads as rank 0: unmaxed, tried in turn, skipped if unreachable
    private static int RankOf(SharedFateRanks ranks, ushort zone) => ranks.Of(zone)?.Rank ?? 0;

    private GemstoneItem? MountItem()
        => cfg.Items.FirstOrDefault(i => i.Kind == GemstoneItemKind.Mount && i.ItemId == cfg.MountItemId);

    private static bool Learned(WorldSnapshot w, uint itemId) => w.LearnedItems?.Contains(itemId) ?? false;

    // Unknown quest state counts as done: the trader's window is the authority (S11)
    private static bool QuestDone(WorldSnapshot w, GemstoneItem i)
        => i.RequiredQuestId == 0 || (w.QuestsDone?.Contains(i.RequiredQuestId) ?? true);

    private static bool Unavailable(WorldSnapshot w, uint itemId) => w.UnavailableItems?.Contains(itemId) ?? false;

    private static bool TraderFailed(WorldSnapshot w, uint traderId) => w.FailedTraders?.Contains(traderId) ?? false;
}
