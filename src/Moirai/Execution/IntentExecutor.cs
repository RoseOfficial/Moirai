using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// Turns planner intents into game and IPC calls. Idempotent per tick: re-issuing
// an in-flight intent is a no-op, and every game call is throttled.
public sealed class IntentExecutor(NavmeshIpc navmesh, CombatIpc combat, Configuration cfg, MinionPurchaser purchaser)
{
    private Vector3? _lastDest;

    public void Execute(Intent intent, WorldSnapshot w)
    {
        switch (intent)
        {
            case GoTo g:
                var destChanged = _lastDest is null || Vector3.Distance(_lastDest.Value, g.Destination) > 0.5f;
                if ((destChanged || !navmesh.PathIsRunning()) && Throttle.Try("moirai.nav", 500))
                {
                    navmesh.MoveCloseTo(g.Destination, g.Fly && cfg.UseFlight, g.Tolerance);
                    _lastDest = g.Destination;
                }
                break;

            case MountUp:
                if (Throttle.Try("moirai.mount", 3000)) GameEx.MountRoulette();
                break;

            case Dismount:
                if (Throttle.Try("moirai.dismount", 1500))
                {
                    navmesh.Stop();
                    GameEx.DismountToggle();
                }
                break;

            case SyncLevel:
                if (!w.Player.IsMounted && Throttle.Try("moirai.sync", 1000)) GameEx.LevelSyncIfNeeded();
                break;

            case Engage e:
                if (Svc.Objects.SearchById(e.TargetId) is { } target && Svc.Targets.Target?.GameObjectId != e.TargetId)
                    Svc.Targets.Target = target;
                break;

            case ClearTarget:
                Svc.Targets.Target = null;
                break;

            case InteractWith i:
                if (Svc.Objects.SearchById(i.ObjectId) is { } obj && Throttle.Try("moirai.interact", 1500))
                {
                    navmesh.Stop();
                    Svc.Targets.Target = obj;
                    GameEx.InteractWith(obj);
                }
                break;

            case TeleportTo:
                // v1 has no aetheryte projection; ChangeZone covers travel
                break;

            case ChangeZone z:
                if (Throttle.Try("moirai.teleport", 8000))
                {
                    navmesh.Stop();
                    ZoneTravel.TeleportToTerritory(z.TerritoryId);
                }
                break;

            case SummonMinion m:
                if (!GameEx.IsCompanionUnlocked(m.MinionId))
                {
                    if (Throttle.Try("moirai.minionmissing", 30000))
                        Svc.Chat.Print("[Moirai] The next yokai's minion isn't owned yet — buy it from Nohi at the Gold Saucer.");
                }
                else if (!w.Player.IsMounted && Throttle.Try("moirai.minion", 3000))
                {
                    GameEx.SummonCompanion(m.MinionId);
                }
                break;

            case AcquireMinion a:
                if (purchaser.HasFailed)
                {
                    if (Throttle.Try("moirai.buy.failed", 60000))
                        Svc.Chat.Print("[Moirai] Auto-buy is stuck — buy the minion manually or toggle auto-buy off.");
                    break;
                }
                purchaser.Begin(a.MinionId, a.MinionItemId); // driven per-frame by the plugin
                break;

            case EquipWatch:
                if (cfg.AutoEquipWatch)
                {
                    if (Throttle.Try("moirai.equipwatch", 3000))
                        Game.GameEx.EquipWristItem(Data.YokaiData.WatchItemId);
                }
                else if (Throttle.Try("moirai.watchmsg", 30000))
                {
                    Svc.Chat.Print("[Moirai] Equip your Yo-kai Watch to start earning medals.");
                }
                break;

            case AcceptReturn:
                if (Throttle.Try("moirai.return", 2000)) GameEx.ClickYes();
                break;

            case SetCombat s:
                combat.Set(s.Enabled, s.Mode);
                break;

            case StopRun:
                navmesh.Stop();
                combat.Set(false, CombatMode.Auto);
                combat.ResetCache();
                _lastDest = null;
                break;

            case Hold or NoAction:
                break;
        }
    }

    public void Reset()
    {
        _lastDest = null;
        combat.ResetCache();
        purchaser.Reset();
    }

    public string? PurchaseStatus => purchaser.HasFailed || purchaser.Status.Length == 0 ? null : purchaser.Status;
}
