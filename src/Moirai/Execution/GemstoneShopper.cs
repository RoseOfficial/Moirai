using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Data;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// S6-S12, S17: runs one gemstone trip end to end: to the trader (a teleport to the aetheryte next to
// them, then a short walk), talk, buy each item, close. Rows are found by item id in the exchange
// window's values (ExchangeRows), never by position or text; Edelina's menu entry and her exchange
// row come from the settings. What goes wrong is put down to what caused it: a fight, a death or
// bags that filled drop the trip and nobody is blamed; a collectible the window lists and will not
// sell is set aside alone; a window that does not match its layout turns shopping off for the
// session; a shop that talks and never opens is remembered as locked; a trip that cannot work at a
// trader skips it for the session; anything else counts as a failed trip against the trader, which
// the planner sets aside for a while and skips once its trips keep failing. Learning what was bought
// is the planner's (S14).
public sealed class GemstoneShopper(NavmeshIpc navmesh, Configuration cfg) : IShellTask
{
    private enum Step { Idle, Travel, Walk, Interact, Menu, Shop, Close }

    public const string CurrencyShop = "ShopExchangeCurrency";
    public const string ItemShop = "ShopExchangeItem";
    public static readonly string[] ShopAddons = [CurrencyShop, ItemShop];
    public static readonly string[] DialogAddons = ["ShopExchangeCurrencyDialog", "ShopExchangeItemDialog"];
    public static readonly string[] MenuAddons = ["SelectString", "SelectIconString"];

    private const long StepTimeoutMs = 20_000;
    private const long TravelTimeoutMs = 45_000; // a teleport is a cast and a loading screen
    private const long WalkStallMs = 10_000;
    private const long MeshWaitMs = 180_000;     // a city vnavmesh has not cached is built on arrival
    private const long CloseForceMs = 5_000;
    private const long MismatchSettleMs = 2_000; // a window still filling can read like one that does not match
    private const int PurchaseFailuresToStop = 3; // S17: purchases in a row that never arrived, whoever the trader
    private const float Reach = 3f;
    private const float InteractReach = 5f;      // near enough to talk once the path ends, as behind a counter
    private const float WalkFromYalms = 250f;    // farther than this from the trader, teleport first
    private const int EdelinaOtherItemsCost = 500; // her Wivre Horn and Fallen Angel Wings, in Endwalker vouchers

    private readonly HashSet<uint> _failedTraders = [];        // S12: no trip can work there this session
    private readonly Dictionary<uint, int> _tripFailures = []; // S12: failed trips in a row, by trader
    private readonly HashSet<uint> _unavailable = [];          // S11
    private bool _shoppingOff;                                 // S17
    private int _purchaseFailures;
    private string _lastFailure = "";
    private List<string> _failureValues = [];
    private Step _step = Step.Idle;
    private long _stepStartedMs;
    private GemstoneTrip? _trip;
    private TraderPlace? _place;
    private ushort _territory;
    private IReadOnlySet<uint> _npcIds = new HashSet<uint>();
    private IReadOnlyCollection<uint> _knownIds = [];
    private Vector3? _destination;
    private int _next;
    private int _countBefore = -1;
    private float _closest;
    private long _closerAtMs;
    private bool _menuPicked;
    private bool _talked;    // the trader was spoken to this trip
    private bool _talkEnded; // and fell silent again with no shop: locked, when it stays that way
    private long? _mismatchSinceMs;

    public IReadOnlySet<uint> FailedTraders => _failedTraders;
    public IReadOnlyDictionary<uint, int> TripFailures => _tripFailures;
    public IReadOnlySet<uint> UnavailableItems => _unavailable;
    public bool ShoppingReady => !_shoppingOff;
    public IReadOnlyList<string> FailureValues => _failureValues; // the exchange window as a purchase or its layout failed
    public bool IsActive => _step != Step.Idle;
    public string Status { get; private set; } = "";
    public string DebugState =>
        $"step={_step} trader={_trip?.TraderId.ToString() ?? "none"} next={_next}/{_trip?.Purchases.Count ?? 0} " +
        $"failures=[{string.Join(",", _tripFailures.Select(f => $"{f.Key}:{f.Value}"))}] skipped=[{string.Join(",", _failedTraders)}] " +
        $"unavailable=[{string.Join(",", _unavailable)}] shopping={(_shoppingOff ? "off" : "on")} talked={_talked} talkEnded={_talkEnded} " +
        $"lastFailure='{_lastFailure}'";

    public void Begin(GemstoneTrip trip)
    {
        if (IsActive || _shoppingOff || _failedTraders.Contains(trip.TraderId)) return;
        _trip = trip;
        if (!GemstoneData.Places.TryGetValue(trip.TraderId, out var place)
            || GemstoneCatalog.Traders.FirstOrDefault(t => t.NpcId == trip.TraderId) is not { } trader)
        {
            FailForSession("the trader is not in the data table");
            return;
        }
        _place = place;
        _territory = trader.TerritoryId;
        _npcIds = new HashSet<uint> { trip.TraderId };
        _knownIds = GemstoneCatalog.Items.Where(i => i.TraderId == trip.TraderId).Select(i => i.ItemId).ToHashSet();
        _destination = null;
        _next = 0;
        _countBefore = -1;
        _menuPicked = false;
        _talked = false;
        _talkEnded = false;
        _mismatchSinceMs = null;
        Enter(Step.Travel);
    }

    // A pause, a stop or a duty drops the trip under way; the planner plans afresh from the counts
    public void Reset()
    {
        if (IsActive) CloseWindows();
        _step = Step.Idle;
        Status = "";
    }

    // A new run forgets the session's failed trips, skipped traders and unsold items
    public void NewSession()
    {
        Reset();
        _failedTraders.Clear();
        _tripFailures.Clear();
        _unavailable.Clear();
        _shoppingOff = false;
        _purchaseFailures = 0;
        _lastFailure = "";
        _failureValues = [];
    }

    public void Tick()
    {
        if (!IsActive || _trip is not { } trip || _place is not { } place) return;

        // S12: a fight or a death on the way is nobody's fault. The trip is dropped, and the planner,
        // which deals with both, plans it again afterwards.
        if (_step is Step.Travel or Step.Walk or Step.Interact && Interruption() is { } what)
        {
            Abort(what);
            return;
        }

        var limit = _step == Step.Travel ? TravelTimeoutMs : StepTimeoutMs;
        if (_step != Step.Walk && Environment.TickCount64 - _stepStartedMs > limit)
        {
            TimedOut(trip);
            return;
        }

        switch (_step)
        {
            case Step.Travel: TickTravel(trip, place); break;
            case Step.Walk: TickWalk(trip, place); break;
            case Step.Interact: TickInteract(trip); break;
            case Step.Menu: TickMenu(place); break;
            case Step.Shop: TickShop(trip); break;
            case Step.Close: TickClose(); break;
        }
    }

    // Combat is read the way the snapshot reads it, so the planner that just asked for the trip in a
    // settled moment and this check never disagree. A teleport already being cast, or under way, is
    // left to finish.
    private static string? Interruption()
    {
        if (Svc.Objects.LocalPlayer is not { } me) return null;
        if (me.IsDead) return "the character died";
        var cond = Svc.Condition;
        return me.StatusFlags.HasFlag(StatusFlags.InCombat) && !cond[ConditionFlag.Casting] && !cond[ConditionFlag.BetweenAreas]
            ? "a fight broke out"
            : null;
    }

    private void TimedOut(GemstoneTrip trip)
    {
        switch (_step)
        {
            // S12: Gadfrid needs Shared FATE rank 3 in every Endwalker zone, Kajeel Ja and Beryl rank 4
            // in every Dawntrail one; their talk says so and no shop follows. A trader who spoke, fell
            // silent and opened nothing is remembered as locked, so later sessions do not travel there.
            case Step.Interact when _talkEnded:
                cfg.GemstoneLockedTraders.Add(trip.TraderId);
                cfg.Save();
                FailForSession("the shop did not open: it is locked for this character, and will not be tried again until you press Try again on the Gemstones tab");
                break;
            case Step.Interact when _talked:
                FailTrip("the trader's talk never finished; is TextAdvance running?");
                break;
            case Step.Interact:
                FailTrip("the trader did not answer");
                break;
            // Only Edelina has a menu, and her entry is a setting: trying again picks the same one
            case Step.Menu:
                FailForSession("the menu entry did not open the exchange; /moirai debug with the menu open lists the entries");
                break;
            case Step.Shop when _countBefore >= 0:
                PurchaseFailed(trip);
                break;
            case Step.Shop:
                FailTrip("the exchange window never showed its stock");
                break;
            default:
                FailTrip($"{_step} timed out");
                break;
        }
    }

    // To the trader's zone and near the trader: a teleport to the aetheryte next to them when not
    // there yet, or when the trader is far off (a farming zone is miles across)
    private void TickTravel(GemstoneTrip trip, TraderPlace place)
    {
        Status = $"going to {TraderName(trip.TraderId)}";
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.Casting]) return;
        var me = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        if (Svc.ClientState.TerritoryType == _territory && Horizontal(me, place.Position) <= WalkFromYalms)
        {
            Enter(Step.Walk);
            return;
        }
        if (Throttle.Try("moirai.gem.teleport", 10_000))
        {
            navmesh.Stop();
            // an aetheryte not attuned stays that way; a teleport refused for now is asked again until
            // the step's time runs out
            if (!ZoneTravel.IsAttuned(place.AetheryteId))
                FailForSession($"aetheryte {place.AetheryteId} next to the trader is not attuned");
            else
                ZoneTravel.TeleportToAetheryte(place.AetheryteId);
        }
    }

    // A short walk: to the trader once they are in view, to the table position until then
    private void TickWalk(GemstoneTrip trip, TraderPlace place)
    {
        Status = $"walking to {TraderName(trip.TraderId)}";
        var now = Environment.TickCount64;
        if (!navmesh.IsReady())
        {
            // a mesh still loading or building holds the walk, and its stall clock with it
            Status = $"waiting for the navmesh to walk to {TraderName(trip.TraderId)}";
            _closerAtMs = now;
            if (now - _stepStartedMs > MeshWaitMs) FailTrip("the navmesh was not ready");
            return;
        }

        var me = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        var npc = GameEx.FindNpc(_npcIds);
        var dest = npc?.Position ?? Destination(place, me);
        var dist = Vector3.Distance(me, dest);
        var pathEnded = !navmesh.PathIsRunning();
        if (dist <= Reach || (npc is not null && dist <= InteractReach && pathEnded))
        {
            navmesh.Stop();
            if (npc is null)
                FailTrip("the trader is not where the data says");
            else
                Enter(Step.Interact);
            return;
        }

        if (dist < _closest - 1f)
        {
            _closest = dist;
            _closerAtMs = now;
        }
        else if (now - _closerAtMs > WalkStallMs)
        {
            FailTrip("the walk stopped getting closer");
            return;
        }
        if (pathEnded && Throttle.Try("moirai.gem.walk", 1000))
            navmesh.MoveCloseTo(dest, false, Reach - 0.5f);
        if (Throttle.Try("moirai.gem.sprint", 1000))
            GameEx.SprintIfReady();
    }

    // The table position, with its height from the mesh when the data has none (Edelina)
    private Vector3 Destination(TraderPlace place, Vector3 me)
    {
        if (!place.HeightUnknown) return place.Position;
        _destination ??= navmesh.NearestPoint(place.Position with { Y = me.Y }, 5f, 200f);
        return _destination ?? place.Position with { Y = me.Y };
    }

    private void TickInteract(GemstoneTrip trip)
    {
        Status = $"talking to {TraderName(trip.TraderId)}";
        if (ShopAddons.Any(GameEx.AddonReady))
        {
            Enter(Step.Shop);
            return;
        }
        if (MenuAddons.Any(GameEx.AddonReady))
        {
            Enter(Step.Menu);
            return;
        }
        // The talk before the window is TextAdvance's. A conversation shows as either occupied flag or
        // as the talk window itself: a field report had Gadfrid's refusal pass unseen with one flag alone.
        if (Svc.Condition[ConditionFlag.OccupiedInQuestEvent] || Svc.Condition[ConditionFlag.OccupiedInEvent] || GameEx.AddonVisible("Talk"))
        {
            _talked = true;
            return;
        }
        if (_talked) _talkEnded = true;
        if (GameEx.FindNpc(_npcIds) is { } npc && Throttle.Try("moirai.gem.interact", 2000))
        {
            Svc.Targets.Target = npc;
            GameEx.InteractWith(npc);
        }
    }

    // Only Edelina has a menu before her exchange: her entry comes from the settings. A menu anywhere
    // else is not one the data knows, so the trader is skipped with the debug hint.
    private void TickMenu(TraderPlace place)
    {
        Status = "picking the menu entry";
        if (ShopAddons.Any(GameEx.AddonReady))
        {
            Enter(Step.Shop);
            return;
        }
        var menu = MenuAddons.FirstOrDefault(GameEx.AddonReady);
        if (menu is null) return;
        if (!place.HasMenu)
        {
            FailForSession($"an unexpected menu ({menu}) opened");
            return;
        }
        if (!_menuPicked && Throttle.Try("moirai.gem.menu", 800))
        {
            GameEx.FireAddonCallback(menu, cfg.GemstoneEdelinaMenuIndex);
            _menuPicked = true;
        }
    }

    private void TickShop(GemstoneTrip trip)
    {
        if (_next >= trip.Purchases.Count)
        {
            Enter(Step.Close);
            return;
        }
        var purchase = trip.Purchases[_next];
        Status = $"buying {ItemName(purchase.ItemId)}";

        // the exchange confirms in a dialog or a yes/no
        var dialog = DialogAddons.FirstOrDefault(GameEx.AddonReady);
        if (dialog is not null)
        {
            if (Throttle.Try("moirai.gem.dialog", 800)) GameEx.FireAddonCallback(dialog, 0);
            return;
        }
        if (GameEx.AddonReady("SelectYesno"))
        {
            if (Throttle.Try("moirai.gem.yes", 800)) GameEx.ClickYes();
            return;
        }

        // asked for: done once the count moves (the step's timeout bounds the wait)
        if (_countBefore >= 0)
        {
            if (GameEx.ItemCount(purchase.ItemId) > _countBefore)
            {
                _purchaseFailures = 0;
                NextPurchase();
            }
            return;
        }

        var shop = ShopAddons.FirstOrDefault(GameEx.AddonReady);
        if (shop is null) return;
        // S18: an item not held yet needs a free bag slot, or the count never moves. The planner plans
        // within the slots it saw; bags that filled since are nobody's fault, and it plans again.
        if (GameEx.ItemCount(purchase.ItemId) == 0 && GameEx.EmptyBagSlots() == 0)
        {
            Abort("the bags are full");
            return;
        }
        if (RowFor(shop, trip, purchase) is not { } row) return;
        if (Throttle.Try("moirai.gem.buy", 2500))
        {
            _countBefore = GameEx.ItemCount(purchase.ItemId);
            GameEx.FireAddonCallback(shop, 0, row, purchase.Quantity);
            _stepStartedMs = Environment.TickCount64; // each purchase gets its own time
        }
    }

    // The row that buys the purchase, or null (waiting, skipped, or failed): by item id in the
    // currency exchange; in Edelina's item exchange, whose window layout is not pinned, the item's
    // place in her shop's list, and only while a wrong row could not buy one of her other items instead
    private int? RowFor(string shop, GemstoneTrip trip, GemstonePurchase purchase)
    {
        if (shop == ItemShop)
        {
            if (trip.TraderId != GemstoneCatalog.Edelina)
            {
                FailForSession($"an item exchange opened at {TraderName(trip.TraderId)}");
                return null;
            }
            if (GameEx.ItemCount(GemstoneCatalog.EndwalkerVoucher) >= EdelinaOtherItemsCost)
            {
                FailForSession("500 or more Endwalker vouchers are held, so a wrong row could buy the Wivre Horn; buy the whistle by hand, or pin the row with /moirai debug");
                return null;
            }
            // S9: the whistle's place in her list comes from the game's own shop data (the third
            // entry as of 7.x); a row set in the settings stands in for it
            var listed = cfg.GemstoneEdelinaRow >= 0
                ? cfg.GemstoneEdelinaRow
                : GameEx.SpecialShopRow(GemstoneData.EdelinaVoucherShop, purchase.ItemId);
            if (listed < 0)
            {
                FailForSession("the game's data does not list the whistle in her exchange; set its row on the Gemstones tab");
                return null;
            }
            return listed;
        }

        var cost = GemstoneCatalog.Items.FirstOrDefault(i => i.TraderId == trip.TraderId && i.ItemId == purchase.ItemId)?.Cost ?? -1;
        var (outcome, row) = ExchangeRows.Find(GameEx.AddonUInts(shop), purchase.ItemId, cost, _knownIds);
        if (outcome != ExchangeRows.Outcome.Mismatch) _mismatchSinceMs = null;
        switch (outcome)
        {
            case ExchangeRows.Outcome.Listed:
                return row;
            case ExchangeRows.Outcome.NotListed:
                _unavailable.Add(purchase.ItemId); // S11
                NextPurchase();
                return null;
            case ExchangeRows.Outcome.Mismatch:
                // S17: a window that stays this way is a layout a patch moved, at every trader alike
                var now = Environment.TickCount64;
                _mismatchSinceMs ??= now;
                if (now - _mismatchSinceMs >= MismatchSettleMs)
                {
                    KeepWindow();
                    ShoppingOff("the exchange window's values do not match its layout");
                }
                return null;
            default:
                return null; // not filled yet
        }
    }

    // S11/S17: the window listed the item, the purchase was asked for, and it never arrived
    private void PurchaseFailed(GemstoneTrip trip)
    {
        var purchase = trip.Purchases[_next];
        KeepWindow();
        if (++_purchaseFailures >= PurchaseFailuresToStop)
        {
            ShoppingOff($"{PurchaseFailuresToStop} purchases in a row did not go through");
            return;
        }
        var unlock = GemstoneCatalog.Items.Any(i => i.ItemId == purchase.ItemId && i.Kind is GemstoneItemKind.Collectible or GemstoneItemKind.Mount);
        if (!unlock)
        {
            FailTrip($"buying {ItemName(purchase.ItemId)} did not go through");
            return;
        }
        // a collectible its trader lists and will not sell (a quest or a rank it still wants, one held
        // somewhere already) is set aside alone, and the rest of the trip goes on
        _unavailable.Add(purchase.ItemId);
        Svc.Chat.Print($"[Moirai] {ItemName(purchase.ItemId)} could not be bought from {TraderName(trip.TraderId)}; it is set aside for this session.");
        NextPurchase();
    }

    // What the exchange window held as a purchase or its layout failed, for the debug report: by the
    // time a report is asked for the window is closed
    private void KeepWindow()
    {
        if (ShopAddons.FirstOrDefault(GameEx.AddonReady) is { } shop)
            _failureValues = GameEx.AddonValues(shop);
    }

    private void NextPurchase()
    {
        _next++;
        _countBefore = -1;
        _mismatchSinceMs = null;
        _stepStartedMs = Environment.TickCount64;
    }

    private void TickClose()
    {
        Status = "closing the exchange";
        var open = ShopAddons.Concat(MenuAddons).FirstOrDefault(GameEx.AddonVisible);
        if (open is null)
        {
            _step = Step.Idle;
            Status = "trip done";
            _tripFailures.Remove(_trip!.TraderId); // S12: a trip that worked starts the trader's count over
            Svc.Chat.Print($"[Moirai] Shopping at {TraderName(_trip!.TraderId)} done.");
            return;
        }
        if (Environment.TickCount64 - _stepStartedMs > CloseForceMs)
        {
            if (Throttle.Try("moirai.gem.closeforce", 1000)) GameEx.CloseAddon(open);
        }
        else if (Throttle.Try("moirai.gem.close", 1000))
        {
            GameEx.FireAddonCallback(open, true, -1);
        }
    }

    private void Enter(Step step)
    {
        _step = step;
        _stepStartedMs = Environment.TickCount64;
        if (step == Step.Walk)
        {
            _closest = float.MaxValue;
            _closerAtMs = _stepStartedMs;
        }
    }

    // S12: a trip that failed for none of the reasons with a rule of their own counts against the
    // trader; the planner sets it aside for a while, and skips it once its trips keep failing
    private void FailTrip(string why)
    {
        if (_trip is { } trip) _tripFailures[trip.TraderId] = _tripFailures.GetValueOrDefault(trip.TraderId) + 1;
        End($"shopping failed: {why}", $"Shopping at {Trader()} failed ({why}).");
    }

    // S12: a trip that cannot work at this trader however often it is tried
    private void FailForSession(string why)
    {
        if (_trip is { } trip) _failedTraders.Add(trip.TraderId);
        End($"shopping failed: {why}", $"Shopping at {Trader()} failed ({why}); {Trader()} is skipped for this session.");
    }

    // S12/S18: nobody's fault; the trip is dropped and the planner plans again. Said in chat once in a
    // while only, in case the same trip is put off again straight away.
    private void Abort(string why)
        => End($"shopping put off: {why}", Throttle.Try("moirai.gem.putoff", 10_000) ? $"Shopping at {Trader()} put off: {why}." : null);

    // S17: the exchange is not what this version knows, at every trader alike
    private void ShoppingOff(string why)
    {
        _shoppingOff = true;
        End($"shopping is off: {why}", $"Shopping is off for this session: {why}. /moirai debug shows what the window held.");
    }

    private void End(string status, string? chat)
    {
        _lastFailure = $"{DateTime.Now:HH:mm:ss} {Trader()} at {_step}: {status}";
        CloseWindows();
        navmesh.Stop();
        _step = Step.Idle;
        Status = status;
        if (chat is not null) Svc.Chat.Print($"[Moirai] {chat}");
    }

    private static void CloseWindows()
    {
        foreach (var name in DialogAddons.Concat(ShopAddons).Concat(MenuAddons))
            if (GameEx.AddonVisible(name))
                GameEx.FireAddonCallback(name, true, -1);
    }

    private static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private string Trader() => _trip is { } trip ? TraderName(trip.TraderId) : "the trader";

    private static string TraderName(uint id) => GemstoneCatalog.Traders.FirstOrDefault(t => t.NpcId == id)?.Name ?? id.ToString();

    private static string ItemName(uint id) => GemstoneCatalog.Items.FirstOrDefault(i => i.ItemId == id)?.Name ?? id.ToString();
}
