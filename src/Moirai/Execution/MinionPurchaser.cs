using System.Numerics;
using Moirai.Data;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// E9: buys a yokai minion from Nohi at the Gold Saucer and learns it. Every step is a game
// window, so this runs per frame from the plugin, outside the planner's busy guard. Nothing here
// reads menu text: entries are picked by a configured index path, the exchange row by the
// minion's position in the shop, and every window's values go to the debug report so a report
// with Nohi's menu open pins the indices. A step that does not finish in its time fails with a
// chat line, and the failure switches auto-buy off for the run.
public sealed class MinionPurchaser(NavmeshIpc navmesh, Configuration cfg)
{
    private enum Step { Idle, GoToZone, WalkToNohi, Interact, Menu, Shop, Close, Learn, Done, Failed }

    public static readonly string[] MenuAddons = ["SelectString", "SelectIconString"];
    public static readonly string[] ShopAddons = ["ShopExchangeItem", "ShopExchangeCurrency"];
    public const string ShopDialogAddon = "ShopExchangeItemDialog";

    private const long StepTimeoutMs = 20_000;
    private const float NohiReach = 3f;

    private Step _step = Step.Idle;
    private long _stepStartedMs;
    private int _menuDepth;
    private uint _minionId;
    private uint _itemId;

    public bool IsActive => _step is not (Step.Idle or Step.Failed or Step.Done);
    public bool HasFailed => _step == Step.Failed;
    public string Status { get; private set; } = "";
    public string DebugState => $"step={_step} minion={_minionId} item={_itemId} menuDepth={_menuDepth}";

    public void Begin(uint minionId, uint itemId)
    {
        if (IsActive || HasFailed) return;
        _minionId = minionId;
        _itemId = itemId;
        _menuDepth = 0;
        Enter(Step.GoToZone);
    }

    public void Reset()
    {
        _step = Step.Idle;
        Status = "";
    }

    public void Tick()
    {
        if (!IsActive) return;
        if (Environment.TickCount64 - _stepStartedMs > StepTimeoutMs)
        {
            Fail($"{_step} timed out");
            return;
        }

        // The minion already in the bags or already learned short-circuits everything
        if (_step is Step.GoToZone or Step.WalkToNohi or Step.Interact or Step.Menu or Step.Shop)
        {
            if (GameEx.IsCompanionUnlocked(_minionId) && GameEx.ItemCount(_itemId) == 0 && _step != Step.GoToZone)
            {
                Enter(Step.Done);
                return;
            }
            if (GameEx.ItemCount(_itemId) > 0)
            {
                Enter(Step.Close);
                return;
            }
        }

        switch (_step)
        {
            case Step.GoToZone:
                Status = "going to the Gold Saucer";
                if (Svc.ClientState.TerritoryType == YokaiData.GoldSaucerTerritory)
                {
                    Enter(Step.WalkToNohi);
                    break;
                }
                if (Throttle.Try("moirai.buy.teleport", 8000))
                {
                    navmesh.Stop();
                    ZoneTravel.TeleportToTerritory(YokaiData.GoldSaucerTerritory);
                    _stepStartedMs = Environment.TickCount64; // a teleport is a loading screen; give it its own time
                }
                break;

            case Step.WalkToNohi:
                Status = "walking to Nohi";
                var target = GameEx.FindNpc(YokaiData.NohiNpcIds)?.Position ?? GameEx.NpcPlacement(YokaiData.NohiNpcIds)?.Position;
                if (target is not { } dest)
                {
                    Fail("Nohi's placement is not in the game data");
                    break;
                }
                var me = Svc.Objects.LocalPlayer?.Position ?? Vector3.Zero;
                if (Vector3.Distance(me, dest) <= NohiReach)
                {
                    navmesh.Stop();
                    Enter(Step.Interact);
                    break;
                }
                if (!navmesh.PathIsRunning() && Throttle.Try("moirai.buy.walk", 1000))
                    navmesh.MoveCloseTo(dest, false, NohiReach);
                _stepStartedMs = Environment.TickCount64; // the walk is bounded by the navmesh, not by us
                break;

            case Step.Interact:
                Status = "talking to Nohi";
                if (MenuAddons.Any(GameEx.AddonReady) || ShopAddons.Any(GameEx.AddonReady))
                {
                    Enter(Step.Menu);
                    break;
                }
                if (GameEx.FindNpc(YokaiData.NohiNpcIds) is { } nohi && Throttle.Try("moirai.buy.interact", 2000))
                {
                    Svc.Targets.Target = nohi;
                    GameEx.InteractWith(nohi);
                }
                break;

            case Step.Menu:
                Status = "picking Nohi's menu";
                if (ShopAddons.Any(GameEx.AddonReady))
                {
                    Enter(Step.Shop);
                    break;
                }
                var menu = MenuAddons.FirstOrDefault(GameEx.AddonReady);
                if (menu is null) break; // the Talk in between is TextAdvance's
                if (_menuDepth >= cfg.YokaiNohiMenuPath.Count)
                {
                    Fail("the menu path ran out before the shop opened");
                    break;
                }
                if (Throttle.Try("moirai.buy.menu", 800))
                {
                    GameEx.FireAddonCallback(menu, cfg.YokaiNohiMenuPath[_menuDepth]);
                    _menuDepth++;
                    _stepStartedMs = Environment.TickCount64;
                }
                break;

            case Step.Shop:
                Status = "buying the minion";
                if (GameEx.AddonReady(ShopDialogAddon))
                {
                    if (Throttle.Try("moirai.buy.dialog", 800)) GameEx.FireAddonCallback(ShopDialogAddon, 0);
                    break;
                }
                if (GameEx.AddonReady("SelectYesno"))
                {
                    if (Throttle.Try("moirai.buy.yes", 800)) GameEx.ClickYes();
                    break;
                }
                var shop = ShopAddons.FirstOrDefault(GameEx.AddonReady);
                if (shop is null) break;
                var row = YokaiData.ShopIndexOf(_itemId) + cfg.YokaiShopIndexOffset;
                if (row < 0)
                {
                    Fail("the minion is not in the roster");
                    break;
                }
                if (Throttle.Try("moirai.buy.buy", 2500))
                    GameEx.FireAddonCallback(shop, 0, row, 1);
                break;

            case Step.Close:
                Status = "closing the shop";
                var open = ShopAddons.FirstOrDefault(GameEx.AddonReady);
                if (open is null)
                {
                    Enter(Step.Learn);
                    break;
                }
                if (Throttle.Try("moirai.buy.close", 1000)) GameEx.CloseAddon(open);
                break;

            case Step.Learn:
                Status = "learning the minion";
                if (GameEx.IsCompanionUnlocked(_minionId))
                {
                    Enter(Step.Done);
                    break;
                }
                if (GameEx.ItemCount(_itemId) == 0)
                {
                    Fail("the minion item is gone but the minion is not learned");
                    break;
                }
                if (Throttle.Try("moirai.buy.use", 2500)) GameEx.UseItem(_itemId);
                break;
        }
    }

    private void Enter(Step step)
    {
        _step = step;
        _stepStartedMs = Environment.TickCount64;
        if (step == Step.Done)
        {
            Status = "minion bought";
            Svc.Chat.Print("[Moirai] Minion bought and learned; back to farming.");
            _step = Step.Idle;
        }
    }

    private void Fail(string why)
    {
        _step = Step.Failed;
        Status = $"buying failed: {why}";
        Svc.Chat.Print($"[Moirai] Could not buy the minion on its own ({why}). Buy it from Nohi by hand; farming picks it up once it is learned. Auto-buy is off for this run; /moirai debug with Nohi's menu open shows the entries.");
    }
}
