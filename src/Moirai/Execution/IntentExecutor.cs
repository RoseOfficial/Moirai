using System.Numerics;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// Turns planner intents into game and IPC calls. Idempotent per tick: re-issuing
// an in-flight intent is a no-op, and every game call is throttled.
public sealed class IntentExecutor(NavmeshIpc navmesh, CombatIpc combat, Configuration cfg)
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

            case AcceptReturn:
                if (Throttle.Try("moirai.return", 2000)) GameEx.ClickYes();
                break;

            case SetCombat s:
                if (s is { Enabled: true, Mode: CombatMode.Defensive })
                    navmesh.Stop(); // D3: a stray is fought where we stand
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
    }
}
