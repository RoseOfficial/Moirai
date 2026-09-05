using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Tests;

public static class TestData
{
    public static FateSnapshot Fate(
        uint id = 1, float x = 100, float z = 100, float radius = 60,
        int progress = 0, FatePhase phase = FatePhase.Running,
        FateKind kind = FateKind.Battle, int maxLevel = 50,
        bool bonus = false, bool specialBoss = false, bool continuation = false,
        long startTimeEpoch = 1_000, long timeRemaining = 600, uint eventItemId = 0,
        FateKind? sheetKind = null)
        => new(id, new Vector3(x, 0, z), radius, progress, phase, kind, maxLevel,
               bonus, specialBoss, continuation, startTimeEpoch, timeRemaining, eventItemId,
               sheetKind ?? (kind == FateKind.NpcStart ? (eventItemId != 0 ? FateKind.Collect : FateKind.Battle) : kind));

    public static PlayerSnapshot Player(
        float x = 0, float y = 0, float z = 0, int level = 100, bool melee = true,
        bool dead = false, bool inCombat = false, bool mounted = false, bool flying = false,
        bool casting = false, bool betweenAreas = false, bool jumping = false,
        bool beingMoved = false, bool occupied = false, bool synced = false,
        bool canMount = true, bool canFly = true,
        ulong? targetId = null,
        bool companionSummoned = false, int companionTimeLeft = 0, uint companionStance = 0,
        uint? activeMinionId = null, bool watchEquipped = false, bool watchOwned = false)
        => new(new Vector3(x, y, z), level, melee, dead, inCombat, mounted, flying,
               casting, betweenAreas, jumping, beingMoved, occupied, synced,
               canMount, canFly, targetId, companionSummoned, companionTimeLeft, companionStance,
               activeMinionId, watchEquipped, watchOwned);

    public static WorldSnapshot World(
        long now = 10_000, long nowMs = 0, ushort territory = 0,
        PlayerSnapshot? player = null,
        IReadOnlyList<FateSnapshot>? fates = null,
        IReadOnlyList<Aetheryte>? aetherytes = null,
        IReadOnlyList<EnemySnapshot>? enemies = null,
        IReadOnlyList<InteractableSnapshot>? interactables = null,
        IReadOnlyDictionary<uint, int>? items = null,
        bool navmeshReady = true, bool lifestreamBusy = false,
        DialogKind dialog = DialogKind.None, bool combatReady = true, bool textAdvanceReady = true,
        bool dodgeReady = false, bool danger = false, IReadOnlySet<uint>? ownedMinions = null,
        bool autoBuyReady = true)
        => new(now, nowMs, territory, player ?? Player(), fates ?? [], aetherytes ?? [],
               enemies ?? [], interactables ?? [],
               items ?? new Dictionary<uint, int>(), navmeshReady, lifestreamBusy,
               dialog, combatReady, textAdvanceReady, dodgeReady, danger, ownedMinions, autoBuyReady);

    public static EnemySnapshot Enemy(
        ulong id = 1000, float x = 100, float y = 0, float z = 100, float hitbox = 2f,
        uint fateId = 1, bool alive = true, bool peels = false, bool attacksMe = false, uint maxHp = 1_000)
        => new(id, new Vector3(x, y, z), hitbox, fateId, alive, peels, attacksMe, maxHp);

    public static InteractableSnapshot Thing(
        ulong id = 2000, float x = 100, float y = 0, float z = 100,
        uint fateId = 1, InteractableKind kind = InteractableKind.Collectable)
        => new(id, new Vector3(x, y, z), fateId, kind);
}
