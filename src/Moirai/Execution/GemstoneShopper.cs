using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Data;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// S6-S12: runs one gemstone trip end to end: to the trader (a teleport to the aetheryte next to
// them, then a short walk), talk, buy each item, close. Rows are found by item id in the exchange
// window's values (ExchangeRows), never by position or text; Edelina's menu entry and her exchange
// row come from the settings. A step that does not finish fails the trip, and the trader is skipped
// for the session; an item the window does not list is skipped for the session without failing it.
// Learning what was bought is the planner's (S14).
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
    private const long CloseForceMs = 5_000;
    private const float Reach = 3f;
    private const float WalkFromYalms = 250f;    // farther than this from the trader, teleport first
    private const int EdelinaOtherItemsCost = 500; // her Wivre Horn and Fallen Angel Wings, in Endwalker vouchers

    private readonly HashSet<uint> _failedTraders = [];
    private readonly HashSet<uint> _unavailable = [];
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

    public IReadOnlySet<uint> FailedTraders => _failedTraders;
    public IReadOnlySet<uint> UnavailableItems => _unavailable;
    public bool IsActive => _step != Step.Idle;
    public string Status { get; private set; } = "";
    public string DebugState =>
        $"step={_step} trader={_trip?.TraderId.ToString() ?? "none"} next={_next}/{_trip?.Purchases.Count ?? 0} " +
        $"failed=[{string.Join(",", _failedTraders)}] unavailable=[{string.Join(",", _unavailable)}]";

    public void Begin(GemstoneTrip trip)
    {
        if (IsActive || _failedTraders.Contains(trip.TraderId)) return;
        _trip = trip;
        if (!GemstoneData.Places.TryGetValue(trip.TraderId, out var place)
            || GemstoneCatalog.Traders.FirstOrDefault(t => t.NpcId == trip.TraderId) is not { } trader)
        {
            Fail("the trader is not in the data table");
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
        Enter(Step.Travel);
    }

    // A pause, a stop or a duty drops the trip under way; the planner plans afresh from the counts
    public void Reset()
    {
        if (IsActive) CloseWindows();
        _step = Step.Idle;
        Status = "";
    }

    // A new run forgets the session's failed traders and unlisted items
    public void NewSession()
    {
        Reset();
        _failedTraders.Clear();
        _unavailable.Clear();
    }

    public void Tick()
    {
        if (!IsActive || _trip is not { } trip || _place is not { } place) return;
        var limit = _step == Step.Travel ? TravelTimeoutMs : StepTimeoutMs;
        if (_step != Step.Walk && Environment.TickCount64 - _stepStartedMs > limit)
        {
            Fail(_step == Step.Shop
                ? "the exchange did not go through; its values may have moved in a patch"
                : $"{_step} timed out");
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
            if (!ZoneTravel.TeleportToAetheryte(place.AetheryteId))
                Fail($"could not teleport to aetheryte {place.AetheryteId}; is it attuned?");
        }
    }

    // A short walk: to the trader once they are in view, to the table position until then
    private void TickWalk(GemstoneTrip trip, TraderPlace place)
    {
        Status = $"walking to {TraderName(trip.TraderId)}";
        var me = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
        var npc = GameEx.FindNpc(_npcIds);
        var dest = npc?.Position ?? Destination(place, me);
        var dist = Vector3.Distance(me, dest);
        if (dist <= Reach)
        {
            navmesh.Stop();
            if (npc is null)
                Fail("the trader is not where the data says");
            else
                Enter(Step.Interact);
            return;
        }

        var now = Environment.TickCount64;
        if (dist < _closest - 1f)
        {
            _closest = dist;
            _closerAtMs = now;
        }
        else if (now - _closerAtMs > WalkStallMs)
        {
            Fail("the walk stopped getting closer");
            return;
        }
        if (!navmesh.PathIsRunning() && Throttle.Try("moirai.gem.walk", 1000))
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
        if (Svc.Condition[ConditionFlag.OccupiedInEvent]) return; // the talk before the window is TextAdvance's
        if (GameEx.FindNpc(_npcIds) is { } npc && Throttle.Try("moirai.gem.interact", 2000))
        {
            Svc.Targets.Target = npc;
            GameEx.InteractWith(npc);
        }
    }

    // Only Edelina has a menu before her exchange: her entry comes from the settings. A menu anywhere
    // else is not one the data knows, so the trip fails with the debug hint.
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
            Fail($"an unexpected menu ({menu}) opened");
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
            if (GameEx.ItemCount(purchase.ItemId) > _countBefore) NextPurchase();
            return;
        }

        var shop = ShopAddons.FirstOrDefault(GameEx.AddonReady);
        if (shop is null) return;
        if (RowFor(shop, trip, purchase) is not { } row) return;
        if (Throttle.Try("moirai.gem.buy", 2500))
        {
            _countBefore = GameEx.ItemCount(purchase.ItemId);
            GameEx.FireAddonCallback(shop, 0, row, purchase.Quantity);
            _stepStartedMs = Environment.TickCount64; // each purchase gets its own time
        }
    }

    // The row that buys the purchase, or null (waiting, skipped, or failed): by item id in the
    // currency exchange; in Edelina's item exchange, whose layout is not pinned, the row from the
    // settings, and only while a wrong row could not buy one of her other items instead
    private int? RowFor(string shop, GemstoneTrip trip, GemstonePurchase purchase)
    {
        if (shop == ItemShop)
        {
            if (trip.TraderId != GemstoneCatalog.Edelina)
            {
                Fail($"an item exchange opened at {TraderName(trip.TraderId)}");
                return null;
            }
            if (GameEx.ItemCount(GemstoneCatalog.EndwalkerVoucher) >= EdelinaOtherItemsCost)
            {
                Fail("500 or more Endwalker vouchers are held, so a wrong row could buy the Wivre Horn; buy the whistle by hand, or pin the row with /moirai debug");
                return null;
            }
            return cfg.GemstoneEdelinaRow;
        }

        var cost = GemstoneCatalog.Items.FirstOrDefault(i => i.TraderId == trip.TraderId && i.ItemId == purchase.ItemId)?.Cost ?? -1;
        var (outcome, row) = ExchangeRows.Find(GameEx.AddonUInts(shop), purchase.ItemId, cost, _knownIds);
        switch (outcome)
        {
            case ExchangeRows.Outcome.Listed:
                return row;
            case ExchangeRows.Outcome.NotListed:
                _unavailable.Add(purchase.ItemId); // S11
                NextPurchase();
                return null;
            case ExchangeRows.Outcome.Mismatch:
                Fail("the exchange window's values do not match its layout; /moirai debug with it open shows them");
                return null;
            default:
                return null; // not filled yet
        }
    }

    private void NextPurchase()
    {
        _next++;
        _countBefore = -1;
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

    private void Fail(string why)
    {
        var name = _trip is { } t ? TraderName(t.TraderId) : "the trader";
        if (_trip is { } trip) _failedTraders.Add(trip.TraderId); // S12
        CloseWindows();
        navmesh.Stop();
        _step = Step.Idle;
        Status = $"shopping failed: {why}";
        Svc.Chat.Print($"[Moirai] Shopping at {name} failed ({why}); {name} is skipped for this session. /moirai debug with the window open shows what it held.");
    }

    private static void CloseWindows()
    {
        foreach (var name in DialogAddons.Concat(ShopAddons).Concat(MenuAddons))
            if (GameEx.AddonVisible(name))
                GameEx.FireAddonCallback(name, true, -1);
    }

    private static float Horizontal(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    private static string TraderName(uint id) => GemstoneCatalog.Traders.FirstOrDefault(t => t.NpcId == id)?.Name ?? id.ToString();

    private static string ItemName(uint id) => GemstoneCatalog.Items.FirstOrDefault(i => i.ItemId == id)?.Name ?? id.ToString();
}
