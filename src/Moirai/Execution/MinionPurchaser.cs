using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;
using Moirai.Data;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// Buys a yokai minion from Nohi at the Gold Saucer and learns it.
// A shell-side driver: the core decides WHEN to buy; this sequences the game steps.
public sealed class MinionPurchaser(NavmeshIpc navmesh)
{
    private enum Step { Idle, GoToZone, WalkToNohi, Interact, Buy, Confirm, CloseShop, Learn, Failed }

    // Nohi's exchange may surface as either shop addon depending on event state
    private static readonly string[] ShopAddons = ["ShopExchangeItem", "ShopExchangeCurrency"];
    private static readonly string[] MenuAddons = ["SelectString", "SelectIconString"];
    private const long StepTimeoutMs = 90_000;

    private Step _step = Step.Idle;
    private uint _minionId;
    private uint _itemId;
    private long _stepStarted;

    public bool HasFailed => _step == Step.Failed;
    public string Status { get; private set; } = "";
    public string DebugState => $"step={_step} minion={_minionId} item={_itemId}";

    public bool IsActive => _step is not (Step.Idle or Step.Failed);

    public void Reset()
    {
        _step = Step.Idle;
        _minionId = 0;
    }

    // Called from the intent stream; the actual work runs in Tick(), which the
    // plugin drives every frame — interacting with NPCs sets "occupied" conditions
    // that hold the planner, so the purchase must keep ticking independently.
    public void Begin(uint minionId, uint minionItemId)
    {
        if (_minionId == minionId) return;
        _minionId = minionId;
        _itemId = minionItemId;
        _menuMisses = 0;
        Enter(Step.GoToZone);
    }

    public void Tick()
    {
        if (_step is Step.Idle or Step.Failed) return;
        if (Environment.TickCount64 - _stepStarted > StepTimeoutMs)
        {
            Fail("timed out");
            return;
        }

        // Nohi chats before (and after) doing business — advance any dialogue first
        if (GameEx.IsAddonVisible("Talk"))
        {
            Status = "skipping dialogue";
            if (Throttle.Try("moirai.buy.talk", 400))
                GameEx.ClickTalk();
            return;
        }

        // already learned (e.g. bought manually mid-run)
        if (GameEx.IsCompanionUnlocked(_minionId) && _step != Step.Learn && GameEx.ItemCount(_itemId) == 0)
        {
            Reset();
            return;
        }

        switch (_step)
        {
            case Step.GoToZone:
                if (GameEx.ItemCount(_itemId) > 0) { Enter(Step.Learn); break; } // bought earlier, never used
                if (Svc.ClientState.TerritoryType == YokaiData.GoldSaucerTerritory) { Enter(Step.WalkToNohi); break; }
                Status = "teleporting to the Gold Saucer";
                if (Throttle.Try("moirai.buy.tp", 8000))
                    ZoneTravel.TeleportToTerritory(YokaiData.GoldSaucerTerritory);
                break;

            case Step.WalkToNohi:
                var nohi = FindNohi();
                var target = nohi?.Position ?? NohiLevelPosition();
                if (target is not { } dest) { Fail("Nohi's placement not found"); break; }
                var dist = Vector3.Distance(Svc.Objects.LocalPlayer?.Position ?? default, dest);
                if (nohi is not null && dist <= 4f)
                {
                    navmesh.Stop();
                    Enter(Step.Interact);
                    break;
                }
                Status = "walking to Nohi";
                if (!navmesh.PathIsRunning() && Throttle.Try("moirai.buy.walk", 1000))
                    navmesh.MoveCloseTo(dest, false, 3.5f);
                break;

            case Step.Interact:
                if (VisibleShop() is not null) { Enter(Step.Buy); break; }
                if (TrySelectMenuEntry()) break;
                Status = "talking to Nohi";
                if (FindNohi() is { } npc && Throttle.Try("moirai.buy.interact", 2000))
                {
                    Svc.Targets.Target = npc;
                    GameEx.InteractWith(npc);
                }
                break;

            case Step.Buy:
                if (GameEx.ItemCount(_itemId) > 0) { Enter(Step.CloseShop); break; }
                if (VisibleShop() is not { } shop) { Enter(Step.Interact); break; }
                var index = YokaiData.ShopIndexOf(_itemId);
                if (index < 0) { Fail("unknown shop index"); break; }
                Status = "buying the minion";
                if (Throttle.Try("moirai.buy.buy", 2000))
                {
                    GameEx.FireAddonCallback(shop, 0, index, 1);
                    Enter(Step.Confirm);
                }
                break;

            case Step.Confirm:
                if (GameEx.ItemCount(_itemId) > 0) { Enter(Step.CloseShop); break; }
                Status = "confirming the purchase";
                if (Throttle.Try("moirai.buy.confirm", 600) && !GameEx.ClickExchangeConfirm())
                    GameEx.ClickYes();
                if (Environment.TickCount64 - _stepStarted > 10_000)
                    Enter(Step.Buy); // purchase didn't land; try the exchange again
                break;

            case Step.CloseShop:
                Status = "closing the shop";
                if (VisibleShop() is not { } openShop) { Enter(Step.Learn); break; }
                if (Throttle.Try("moirai.buy.close", 1000))
                    GameEx.CloseAddon(openShop);
                break;

            case Step.Learn:
                if (GameEx.IsCompanionUnlocked(_minionId)) { Status = "minion learned"; Reset(); break; }
                if (GameEx.ItemCount(_itemId) == 0) { Fail("minion item vanished before use"); break; }
                Status = "learning the minion";
                if (Throttle.Try("moirai.buy.use", 2000))
                    GameEx.UseItem(_itemId);
                break;
        }
    }

    private static string? VisibleShop()
        => ShopAddons.FirstOrDefault(GameEx.IsAddonVisible);

    private int _menuMisses;

    // Nohi fronts the shop with a "purchase minions" style menu; pick by text, never by index.
    private bool TrySelectMenuEntry()
    {
        foreach (var menu in MenuAddons)
        {
            if (!GameEx.IsAddonVisible(menu)) continue;
            Status = "choosing the exchange";
            if (!Throttle.Try("moirai.buy.menu", 800)) return true;
            var index = GameEx.FindMenuEntry(menu, "minion", out var entries);
            if (index >= 0)
            {
                _menuMisses = 0;
                GameEx.FireAddonCallback(menu, index);
                return true;
            }
            if (entries.Length == 0)
                return true; // the menu opens a frame before its entries populate — wait, don't fail
            if (++_menuMisses >= 5)
                Fail($"no minion option in the menu ({entries})");
            return true;
        }
        return false;
    }

    private void Enter(Step step)
    {
        _step = step;
        _stepStarted = Environment.TickCount64;
    }

    private void Fail(string why)
    {
        _step = Step.Failed;
        Status = $"buying failed: {why}";
        Svc.Chat.Print($"[Moirai] Couldn't buy the minion automatically ({why}) — grab it from Nohi and it'll pick back up.");
    }

    private static IGameObject? FindNohi()
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj is { ObjectKind: ObjectKind.EventNpc } && YokaiData.NohiNpcIds.Contains(obj.BaseId))
                return obj;
        }
        return null;
    }

    private static Vector3? NohiLevelPosition()
    {
        var sheet = Svc.Data.GetExcelSheet<Level>();
        if (sheet is null) return null;
        foreach (var row in sheet)
        {
            if (row.Territory.RowId != YokaiData.GoldSaucerTerritory) continue;
            if (!YokaiData.NohiNpcIds.Contains(row.Object.RowId)) continue;
            return new Vector3(row.X, row.Y, row.Z);
        }
        return null;
    }
}
