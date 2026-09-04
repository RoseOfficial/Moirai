using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Snapshot;

// The one place game state is read. Everything downstream sees an immutable WorldSnapshot.
public sealed class SnapshotBuilder(Configuration cfg, NavmeshIpc navmesh, IReadOnlyList<uint> trackedItems)
{
    public WorldSnapshot? Build(uint? currentFateId)
    {
        var lp = Svc.Objects.LocalPlayer;
        if (lp is null) return null;
        var cond = Svc.Condition;

        var companionTimeLeft = GameEx.CompanionTimeLeftSeconds();
        var currentFate = currentFateId is { } wanted ? Svc.Fates.FirstOrDefault(f => f?.FateId == wanted) : null;
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
            IsLevelSynced: IsLevelSynced(lp, currentFate),
            CanMount: true,  // the executor's mount call is a safe no-op where mounting is illegal
            CanFly: true,    // per-zone no-fly overrides gate flight; vnavmesh grounds the rest
            TargetId: Svc.Targets.Target?.GameObjectId,
            CompanionSummoned: companionTimeLeft > 0,
            CompanionTimeLeftSeconds: companionTimeLeft,
            CompanionStanceId: GameEx.CompanionStanceId());

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
        ScanObjects(lp, CompanionObjectId(), currentFateId, enemies, interactables);

        // Consumables the planner budgets (greens), plus every visible collect fate's
        // event item, since collect fates hand in by item count (B1)
        var items = new Dictionary<uint, int>();
        foreach (var id in trackedItems)
            items[id] = GameEx.ItemCount(id);
        foreach (var f in fates)
            if (f.EventItemId != 0 && !items.ContainsKey(f.EventItemId))
                items[f.EventItemId] = GameEx.ItemCount(f.EventItemId);

        return new WorldSnapshot(
            NowEpoch: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            NowMs: Environment.TickCount64,
            TerritoryId: (ushort)Svc.ClientState.TerritoryType,
            Player: player,
            Fates: fates,
            Aetherytes: [],
            Enemies: enemies,
            Interactables: interactables,
            ItemCounts: items,
            NavmeshReady: navmesh.IsReady(),
            LifestreamBusy: false);
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

        uint eventItem = 0;
        uint rule = 0;
        try
        {
            if (fate.GameData.ValueNullable is { } row)
            {
                if (row.EventItem.RowId != 0) eventItem = row.EventItem.RowId;
                else if (row.TurnInEventItem.RowId != 0) eventItem = row.TurnInEventItem.RowId;
                else if (row.ReqEventItem.RowId != 0) eventItem = row.ReqEventItem.RowId;
                rule = row.Rule;
            }
        }
        catch { /* sheet row unavailable: treat as battle */ }

        // B11: unopened fates of every sheet kind are NPC-start until they open
        var kind = FateClassifier.Classify(eventItem, rule, phase.Value, fate.StartTimeEpoch, fate.Progress);
        var specialBoss = FateClassifier.FromSheet(eventItem, rule) == FateKind.Boss && fate.Level >= 60; // curated special-boss data refines this later

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

    // The chocobo companion's object id, so a mob on it counts as on us; null when it is not out
    private static ulong? CompanionObjectId()
    {
        try { return Svc.Buddies.CompanionBuddy?.GameObject?.GameObjectId; }
        catch { return null; }
    }

    // The current fate's enemies and objectives, plus anything hostile that is on us or our
    // companion wherever it belongs (D3: the planner clears stray aggro itself)
    private static void ScanObjects(IGameObject player, ulong? companionId, uint? fateId, List<EnemySnapshot> enemies, List<InteractableSnapshot> interactables)
    {
        foreach (var obj in Svc.Objects)
        {
            if (obj == null || obj.Address == nint.Zero) continue;
            var objFate = GameEx.GetFateId(obj);
            var ours = fateId is { } f && objFate == f;

            if (obj is IBattleNpc bnpc)
            {
                // a corpse or an untargetable spawn is neither an enemy nor an objective
                if (bnpc.IsDead || bnpc.CurrentHp == 0 || !obj.IsTargetable) continue;
                var onUs = bnpc.TargetObjectId == player.GameObjectId
                           || (companionId is { } c && bnpc.TargetObjectId == c);
                if (!ours && !onUs) continue; // the hostility check is a game call: only for what matters
                // B10: hostility, not sub-kind, splits the fate's enemies from its captives and escortees
                if (GameEx.IsHostile(obj))
                {
                    enemies.Add(new EnemySnapshot(
                        Id: obj.GameObjectId,
                        Position: obj.Position,
                        HitboxRadius: obj.HitboxRadius,
                        FateId: objFate,
                        IsAlive: true,
                        TargetsProtectedFriendly: ours && TargetsFateFriendly(bnpc, objFate),
                        IsAttackingPlayer: onUs));
                }
                else if (ours)
                {
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.ObjectiveNpc));
                }
                continue;
            }

            switch (obj.ObjectKind)
            {
                case ObjectKind.EventObj when ours:
                    interactables.Add(new InteractableSnapshot(obj.GameObjectId, obj.Position, objFate, InteractableKind.Collectable));
                    break;
                case ObjectKind.EventNpc when ours:
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
            && !GameEx.IsHostile(friendly)
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

    // C11: at or below the fate's cap the game offers no sync, so there is none to wait for
    private static unsafe bool IsLevelSynced(IGameObject lp, IFate? currentFate)
    {
        var fm = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager.Instance();
        if (fm != null && fm->SyncedFateId != 0) return true;
        return currentFate is not null && lp is ICharacter c && c.Level <= currentFate.MaxLevel;
    }
}
