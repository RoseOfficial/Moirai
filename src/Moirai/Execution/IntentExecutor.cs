using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// Turns planner intents into game and IPC calls. Idempotent per tick: re-issuing
// an in-flight intent is a no-op, and every game call is throttled.
public sealed class IntentExecutor(NavmeshIpc navmesh, CombatIpc combat, DodgeIpc dodge, Configuration cfg)
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

            case StopMoving:
                // C13: drop the running path and forget it, so the next GoTo is issued as a fresh path
                navmesh.Stop();
                _lastDest = null;
                break;

            case Jump:
                if (Throttle.Try("moirai.jump", 1000)) GameEx.Jump();
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

            case TeleportTo t:
                // C17 and recovery rung 4: an in-zone teleport; the planner holds the intent until it lands
                if (Throttle.Try("moirai.teleport", 8000))
                {
                    navmesh.Stop();
                    ZoneTravel.TeleportToAetheryte(t.AetheryteId);
                }
                break;

            case ChangeZone z:
                if (Throttle.Try("moirai.teleport", 8000))
                {
                    navmesh.Stop();
                    ZoneTravel.TeleportToTerritory(z.TerritoryId);
                }
                break;

            case SummonCompanion g:
                if (Throttle.Try("moirai.companion", 3000))
                {
                    navmesh.Stop(); // an item use mid-path is interrupted, and the green is wasted
                    GameEx.UseItem(g.GreensItemId);
                }
                break;

            case SetCompanionStance s:
                if (Throttle.Try("moirai.stance", 2000)) GameEx.SetBuddyAction(s.StanceActionId);
                break;

            case SummonMinion m:
                if (!w.Player.IsMounted && Throttle.Try("moirai.minion", 3000)) GameEx.SummonMinion(m.MinionId); // E3
                break;

            case EquipWatch:
                if (Throttle.Try("moirai.equipwatch", 3000)) GameEx.EquipWristItem(Data.YokaiData.WatchItemId); // E1
                break;

            case AcceptReturn:
                if (Throttle.Try("moirai.return", 2000)) GameEx.ClickYes();
                break;

            case ConfirmDialog:
                if (Throttle.Try("moirai.confirm", 600)) GameEx.ClickYes(); // B4: the planner decided it is ours
                break;

            case SetCombat s:
                if (s is { Enabled: true, Mode: CombatMode.Defensive })
                    navmesh.Stop(); // D3: a stray is fought where we stand
                combat.Set(s.Enabled, s.Mode);
                dodge.SetAi(s.Enabled); // H1: the dodge layer's AI rides along with the rotation
                break;

            case HandMovementTo h:
                if (h.Owner == MovementOwner.Dodge)
                {
                    navmesh.Stop(); // H2: our path ends here; the dodge layer moves the character
                    _lastDest = null;
                }
                dodge.SetMovement(h.Owner);
                break;

            case StopRun:
                navmesh.Stop();
                combat.Set(false, CombatMode.Auto);
                combat.ResetCache();
                dodge.SetAi(false);
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
        dodge.Reset();
    }
}
