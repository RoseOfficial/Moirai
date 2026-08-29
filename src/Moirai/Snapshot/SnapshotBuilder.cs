using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Snapshot;

// The one place game state is read. Everything downstream sees an immutable WorldSnapshot.
public sealed class SnapshotBuilder(
    Configuration cfg, NavmeshIpc navmesh,
    IReadOnlyList<uint> trackedItems, uint watchItemId, IReadOnlyList<uint> minionIds)
{
    public WorldSnapshot? Build(uint? currentFateId)
    {
        var lp = Svc.Objects.LocalPlayer;
        if (lp is null) return null;
        var cond = Svc.Condition;

        var watchCount = watchItemId == 0 ? 0 : GameEx.ItemCount(watchItemId);
        var player = new PlayerSnapshot(
            Position: lp.Position,
            Level: lp.Level,
            IsMelee: IsMeleeRole(lp),
            IsDead: lp.IsDead,
            InCombat: lp.StatusFlags.HasFlag(StatusFlags.InCombat),
            IsMounted: cond[ConditionFlag.Mounted],
            IsFlying: cond[ConditionFlag.InFlight],
            IsCasting: cond[ConditionFlag.Casting] || cond[ConditionFlag.Casting87],
            IsBetweenAreas: cond[ConditionFlag.BetweenAreas] || cond[ConditionFlag.BetweenAreas51],
            IsJumping: cond[ConditionFlag.Jumping] || cond[ConditionFlag.Jumping61],
            IsBeingMoved: cond[ConditionFlag.BeingMoved],
            IsOccupied: cond[ConditionFlag.Occupied] || cond[ConditionFlag.Occupied30]
                        || cond[ConditionFlag.Occupied33] || cond[ConditionFlag.Occupied38]
                        || cond[ConditionFlag.Occupied39] || cond[ConditionFlag.OccupiedInEvent]
                        || cond[ConditionFlag.OccupiedInQuestEvent] || cond[ConditionFlag.OccupiedSummoningBell]
                        || cond[ConditionFlag.OccupiedInCutSceneEvent],
            IsLevelSynced: IsLevelSynced(),
            CanMount: true,  // executor's mount call is a safe no-op where mounting is illegal
            CanFly: true,    // per-zone no-fly overrides gate flight; vnavmesh grounds the rest
            TargetId: Svc.Targets.Target?.GameObjectId,
            ActiveMinionId: CurrentMinionId(lp),
            YokaiWatchActive: watchCount > 0,
            YokaiWatchOwned: watchCount > 0);

        var fates = new List<FateSnapshot>();
        foreach (var fate in Svc.Fates)
        {
            if (fate == null) continue;
            var snap = Project(fate);
            if (snap is null) continue;
            if (cfg.SkipNpcStartFates && snap.Kind == FateKind.NpcStart && snap.Id != currentFateId) continue;
            fates.Add(snap);
        }

        var enemies = new List<EnemySnapshot>();
        var interactables = new List<InteractableSnapshot>();
        if (currentFateId is { } fid)
            ScanObjects(lp, fid, enemies, interactables);

        var items = new Dictionary<uint, int>();
        foreach (var id in trackedItems)
            items[id] = GameEx.ItemCount(id);

        var owned = new HashSet<uint>();
        foreach (var id in minionIds)
            if (GameEx.IsCompanionUnlocked(id))
                owned.Add(id);

        return new WorldSnapshot(
            NowEpoch: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            TerritoryId: (ushort)Svc.ClientState.TerritoryType,
            Player: player,
            Fates: fates,
            Aetherytes: [],
            Enemies: enemies,
            Interactables: interactables,
            ItemCounts: items,
            NavmeshReady: navmesh.IsReady(),
            LifestreamBusy: false,
            OwnedMinions: owned);
    }

    private static FateSnapshot? Project(IFate fate)
    {
        var phase = fate.State switch
        {
            FateState.Preparing => FatePhase.Preparing,
            FateState.Running => FatePhase.Running,
            FateState.Ended => FatePhase.Ended,
            FateState.Failed => FatePhase.Failed,
            _ => (FatePhase?)null,
        };
        if (phase is null) return null;

        var kind = FateKind.Battle;
        uint eventItem = 0;
        var specialBoss = false;
        try
        {
            if (fate.GameData.ValueNullable is { } row)
            {
                if (row.EventItem.RowId != 0) eventItem = row.EventItem.RowId;
                else if (row.TurnInEventItem.RowId != 0) eventItem = row.TurnInEventItem.RowId;
                else if (row.ReqEventItem.RowId != 0) eventItem = row.ReqEventItem.RowId;

                if (eventItem != 0) kind = FateKind.Collect;
                else kind = row.Rule switch { 4 => FateKind.Boss, 5 => FateKind.Defend, 6 => FateKind.Escort, _ => FateKind.Battle };
                specialBoss = kind == FateKind.Boss && fate.Level >= 60; // curated special-boss data refines this later
            }
        }
        catch { /* sheet row unavailable: treat as battle */ }

        // An unopened fate must be started at its NPC regardless of its sheet type
        if (fate.StartTimeEpoch == 0 && fate.Progress == 0 && kind != FateKind.Collect)
            kind = FateKind.NpcStart;

        return new FateSnapshot(
            Id: fate.FateId,
            Position: fate.Position,
            Radius: fate.Radius,
            Progress: fate.Progress,
            Phase: phase.Value,
            Kind: kind,
            MaxLevel: fate.Level,
            IsBonus: false, // bonus detection lands with the data layer
            IsSpecialBoss: specialBoss,
            HasContinuation: false, // continuation chains land with the data layer
            StartTimeEpoch: fate.StartTimeEpoch,
            TimeRemainingSeconds: fate.TimeRemaining,
            EventItemId: eventItem);
    }

    private static void ScanObjects(IGameObject player, uint fateId, List<EnemySnapshot> enemies, List<InteractableSnapshot> interactables)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj == null || obj.Address == nint.Zero) continue;
            var objFate = GameEx.GetFateId(obj);

            if (obj is IBattleNpc bnpc)
            {
                if (objFate != fateId) continue;
                if (bnpc.BattleNpcKind == BattleNpcSubKind.Combatant)
                {
                    enemies.Add(new EnemySnapshot(
                        Id: obj.GameObjectId,
                        Position: obj.Position,
                        HitboxRadius: obj.HitboxRadius,
                        FateId: objFate,
                        IsAlive: !bnpc.IsDead && bnpc.CurrentHp > 0,
                        TargetsProtectedFriendly: TargetsFateFriendly(bnpc, fateId),
                        IsAttackingPlayer: bnpc.TargetObjectId == player.GameObjectId));
                }
                else
                {
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.ObjectiveNpc));
                }
                continue;
            }

            switch (obj.ObjectKind)
            {
                case ObjectKind.EventObj when objFate == fateId:
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.Collectable));
                    break;
                case ObjectKind.EventNpc when objFate == fateId:
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.ObjectiveNpc));
                    break;
                case ObjectKind.EventNpc or ObjectKind.BattleNpc when GameEx.GetNameplateIcon(obj) != 0:
                    // potential fate-starter; B5 lets the planner match by ring proximity when FateId is 0
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.StarterNpc));
                    break;
            }
        }
    }

    private static bool TargetsFateFriendly(IBattleNpc enemy, uint fateId)
    {
        var target = Svc.Objects.SearchById(enemy.TargetObjectId);
        return target is IBattleNpc friendly
            && friendly.BattleNpcKind != BattleNpcSubKind.Combatant
            && GameEx.GetFateId(target) == fateId;
    }

    private static bool IsMeleeRole(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter lp)
    {
        try
        {
            var role = lp.ClassJob.Value.Role;
            return role is 1 or 2; // tank or melee dps fight at melee range
        }
        catch
        {
            return true;
        }
    }

    private static unsafe bool IsLevelSynced()
    {
        var fm = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager.Instance();
        return fm != null && fm->SyncedFateId != 0;
    }

    private static uint? CurrentMinionId(Dalamud.Game.ClientState.Objects.Types.ICharacter lp)
        => lp.CurrentMinion is { RowId: > 0 } minion ? minion.RowId : null;
}
