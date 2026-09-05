using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

// The overlay's view of the run: the active yokai and its count, plus whatever is being skipped
public sealed record YokaiStatus(string Name, int Legendary, int Cap, string? Note);

// §8: farms legendary medals yokai by yokai (E1–E7). The active yokai is the first in priority
// order under the cap whose minion is owned; its designated zones become a zone rotation; its
// minion is summoned in settled moments, with a bounded grace before the yokai is given up on.
public sealed class YokaiModule(YokaiConfig cfg) : IFarmModule
{
    public const int SummonGraceSeconds = 20; // E3: settled seconds asking for a minion before giving up on it
    public const int EquipGraceSeconds = 10;  // E1: the same for the watch

    private readonly HashSet<uint> _unsummonable = [];
    private long? _summonAskEpoch;
    private long? _equipAskEpoch;
    private bool _watchGivenUp;
    private ZoneRotationModule? _rotation;
    private uint? _rotationFor;

    public YokaiStatus? Status { get; private set; }

    public ModuleDirective Next(WorldSnapshot w, ModuleContext ctx)
    {
        var p = w.Player;

        // E2/E6: fresh counts every call; the first under the cap, owned and summonable, is the target.
        // E9: an unowned one is bought first when auto-buy is on and the medals cover it.
        Yokai? active = null;
        var shopping = new List<string>();
        foreach (var y in InPriorityOrder())
        {
            if (w.CountOf(y.LegendaryMedalItemId) >= cfg.LegendaryCap) continue;
            if (_unsummonable.Contains(y.MinionId)) continue;
            if (!Owned(w, y))
            {
                var price = PriceFor(w);
                if (cfg.AutoBuy && w.AutoBuyReady && y.MinionItemId != 0 && cfg.MedalItemId != 0
                    && w.CountOf(cfg.MedalItemId) >= price)
                {
                    Status = new YokaiStatus(y.Name, 0, cfg.LegendaryCap, Note(p));
                    return new BuyMinion(y.MinionId, y.MinionItemId, price);
                }
                shopping.Add(y.Name);
                continue;
            }
            active = y;
            break;
        }

        var note = Note(p);
        if (active is null)
        {
            Status = new YokaiStatus("", 0, cfg.LegendaryCap, note);
            return shopping.Count > 0
                ? new StopSession(StopReason.MinionsMissing, "Minions not owned: " + string.Join(", ", shopping))
                : new StopSession(StopReason.AllYokaiCapped, Summary(w)); // E5
        }
        Status = new YokaiStatus(active.Name, w.CountOf(active.LegendaryMedalItemId), cfg.LegendaryCap, note);

        // E1: the watch earns regular medals, so it is worn when owned; never worth stopping for
        if (cfg.AutoEquipWatch && p.WatchOwned && !p.WatchEquipped && !_watchGivenUp && ctx.Settled)
        {
            _equipAskEpoch ??= w.NowEpoch;
            if (w.NowEpoch - _equipAskEpoch >= EquipGraceSeconds)
                _watchGivenUp = true;
            else
                return new EnsureWatch();
        }

        // E4/E7: the yokai's designated zones, farmed in turn like any rotation
        if (_rotationFor != active.MinionId)
        {
            _rotation = new ZoneRotationModule(new RotationConfig
            {
                Zones = active.TerritoryIds,
                QuietSeconds = cfg.QuietSeconds,
                MoveTimeoutSeconds = cfg.MoveTimeoutSeconds,
            });
            _rotationFor = active.MinionId;
            _summonAskEpoch = null;
        }
        var zone = _rotation!.Next(w, ctx);
        if (zone is not FarmHere) return zone;

        // E3: the minion must be out for the medals to drop; asked for in settled moments only,
        // and a minion that never shows up through the grace is given up on for the session
        if (p.ActiveMinionId != active.MinionId)
        {
            if (!ctx.Settled)
            {
                _summonAskEpoch = null;
                return new FarmHere();
            }
            _summonAskEpoch ??= w.NowEpoch;
            if (w.NowEpoch - _summonAskEpoch >= SummonGraceSeconds)
            {
                _unsummonable.Add(active.MinionId);
                _summonAskEpoch = null;
                return Next(w, ctx);
            }
            return new EnsureMinion(active.MinionId);
        }
        _summonAskEpoch = null;
        return new FarmHere();
    }

    private static bool Owned(WorldSnapshot w, Yokai y)
        => w.OwnedMinions is null || w.OwnedMinions.Contains(y.MinionId); // null: ownership unknown, so try

    // E9: the event's price rule as a hint, one medal for the first event minion and three after;
    // the shop itself is the authority and a refusal fails the purchase cleanly
    private int PriceFor(WorldSnapshot w)
        => w.OwnedMinions is { } owned && !cfg.Roster.Any(y => owned.Contains(y.MinionId)) ? 1 : 3;

    private IEnumerable<Yokai> InPriorityOrder()
    {
        if (cfg.Priority.Count == 0) return cfg.Roster;
        return cfg.Priority
            .Select(id => cfg.Roster.FirstOrDefault(y => y.MinionId == id))
            .Where(y => y is not null)!;
    }

    private string? Note(PlayerSnapshot p)
    {
        var notes = new List<string>();
        if (!p.WatchEquipped)
            notes.Add(!p.WatchOwned ? "Yo-kai Watch not owned: no regular medals"
                : _watchGivenUp ? "Yo-kai Watch could not be equipped"
                : "Yo-kai Watch not equipped");
        foreach (var id in _unsummonable)
            notes.Add($"{cfg.Roster.FirstOrDefault(y => y.MinionId == id)?.Name ?? id.ToString()} could not be summoned");
        return notes.Count == 0 ? null : string.Join("; ", notes);
    }

    private string Summary(WorldSnapshot w)
        => string.Join(", ", InPriorityOrder().Select(y => $"{y.Name}: {w.CountOf(y.LegendaryMedalItemId)}/{cfg.LegendaryCap}"));
}
