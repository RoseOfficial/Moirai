using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Tests;

public class EngageBehaviorTests
{
    private static BehaviorContext Ctx(FateSnapshot fate)
        => new(fate, true, new FixedRandom(0.5), new FlatGround());

    private static EngageBehavior Sut() => new(new EngageConfig());

    [Fact] // B7: peel enemies attacking protected friendlies first
    public void B7_defend_peel_prefers_protector_threats()
    {
        var peeler = TestData.Enemy(id: 1, x: 10, peels: true);
        var closer = TestData.Enemy(id: 2, x: 1);
        var picked = TargetPicker.Choose([peeler, closer], fateId: 1, sticky: null, playerPos: Vector3.Zero);
        Assert.Equal(1uL, picked!.Id);
    }

    [Fact] // sticky targeting: keep the engaged target while it lives
    public void Sticky_target_kept_until_dead()
    {
        var a = TestData.Enemy(id: 1, x: 50);
        var b = TestData.Enemy(id: 2, x: 1);
        Assert.Equal(1uL, TargetPicker.Choose([a, b], 1, sticky: 1, Vector3.Zero)!.Id);
        var aDead = a with { IsAlive = false };
        Assert.Equal(2uL, TargetPicker.Choose([aDead, b], 1, sticky: 1, Vector3.Zero)!.Id);
    }

    [Fact] // C11: sync only once actually inside the ring
    public void C11_syncs_inside_ring_before_fighting()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var w = TestData.World(player: TestData.Player(synced: false), fates: [fate],
            enemies: [TestData.Enemy(x: 5)]);
        Assert.IsType<SyncLevel>(Sut().Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // C5: knocked out of the ring -> path back to center
    public void C5_reenters_ring_after_knockback()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var w = TestData.World(player: TestData.Player(x: 35, z: 0, synced: true), fates: [fate]);
        var go = Assert.IsType<GoTo>(Sut().Tick(w, Ctx(fate)).Intent);
        Assert.Equal(fate.Position, go.Destination);
    }

    [Fact] // B10: stray target belonging to another fate is cleared when nothing of ours is up
    public void B10_clears_stray_target()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var stray = TestData.Enemy(id: 9, fateId: 2, x: 5);
        var w = TestData.World(player: TestData.Player(synced: true, targetId: 9), fates: [fate], enemies: [stray]);
        Assert.IsType<ClearTarget>(Sut().Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // B10: a stray target is replaced, never cleared, while a fate enemy exists
    public void B10_replaces_stray_target_when_fate_enemy_exists()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var stray = TestData.Enemy(id: 9, fateId: 2, x: 5);
        var ours = TestData.Enemy(id: 7, fateId: 1, x: 3);
        var w = TestData.World(player: TestData.Player(synced: true, targetId: 9), fates: [fate], enemies: [stray, ours]);
        var sut = Sut();
        Assert.IsType<SetCombat>(sut.Tick(w, Ctx(fate)).Intent);
        Assert.Equal(7uL, Assert.IsType<Engage>(sut.Tick(w, Ctx(fate)).Intent).TargetId);
    }

    [Fact] // B10: the fate's own friendly NPC is never held as the target while enemies are up
    public void B10_friendly_fate_npc_target_replaced_with_enemy()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var hostage = TestData.Thing(id: 42, fateId: 1, x: 2, kind: InteractableKind.ObjectiveNpc);
        var captor = TestData.Enemy(id: 7, fateId: 1, x: 4);
        var w = TestData.World(player: TestData.Player(synced: true, targetId: 42), fates: [fate],
            enemies: [captor], interactables: [hostage]);
        var sut = Sut();
        Assert.IsType<SetCombat>(sut.Tick(w, Ctx(fate)).Intent);
        Assert.Equal(7uL, Assert.IsType<Engage>(sut.Tick(w, Ctx(fate)).Intent).TargetId);
    }

    [Fact] // single-owner rule: a valid fate enemy the backend switched to is accepted, not fought over
    public void Backend_target_switch_to_fate_enemy_is_accepted()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var first = TestData.Enemy(id: 1, x: 2, z: 0);
        var second = TestData.Enemy(id: 2, x: 3, z: 0);
        var sut = Sut();
        var untargeted = TestData.World(player: TestData.Player(synced: true), fates: [fate], enemies: [first, second]);
        sut.Tick(untargeted, Ctx(fate)); // SetCombat
        Assert.Equal(1uL, Assert.IsType<Engage>(sut.Tick(untargeted, Ctx(fate)).Intent).TargetId);

        var switched = TestData.World(player: TestData.Player(synced: true, targetId: 2), fates: [fate], enemies: [first, second]);
        Assert.IsNotType<Engage>(sut.Tick(switched, Ctx(fate)).Intent);
    }

    [Fact] // the backend can switch itself off during a lull: fighting re-asserts its mode every tick
    public void Fighting_reasserts_combat_mode_each_tick()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var enemy = TestData.Enemy(id: 7, x: 2, z: 0);
        var w = TestData.World(player: TestData.Player(synced: true, targetId: 7), fates: [fate], enemies: [enemy]);
        var sut = Sut();
        sut.Tick(w, Ctx(fate)); // combat on
        var fighting = Assert.IsType<SetCombat>(sut.Tick(w, Ctx(fate)).Intent);
        Assert.True(fighting.Enabled);
        Assert.Equal(CombatMode.Auto, fighting.Mode);
        Assert.IsType<SetCombat>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // a foreign target is replaced before we start closing distance, so the backend stops hitting it
    public void Target_is_asserted_before_closing_distance()
    {
        var fate = TestData.Fate(id: 1, x: 0, z: 0, radius: 60);
        var stray = TestData.Enemy(id: 9, fateId: 2, x: 5);
        var far = TestData.Enemy(id: 7, fateId: 1, x: 40);
        var w = TestData.World(player: TestData.Player(synced: true, targetId: 9), fates: [fate], enemies: [stray, far]);
        var sut = Sut();
        sut.Tick(w, Ctx(fate)); // SetCombat
        Assert.IsType<Engage>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // combat backend enabled once, then target engaged
    public void Enables_combat_then_engages()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var enemy = TestData.Enemy(id: 7, x: 3, z: 0);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate], enemies: [enemy]);
        var sut = Sut();
        var first = sut.Tick(w, Ctx(fate));
        var on = Assert.IsType<SetCombat>(first.Intent);
        Assert.True(on.Enabled);
        var second = sut.Tick(w, Ctx(fate));
        Assert.Equal(7uL, Assert.IsType<Engage>(second.Intent).TargetId);
    }

    [Fact] // C10: ranged jobs approach only to ranged distance
    public void C10_ranged_distance_respected()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 60);
        var enemy = TestData.Enemy(id: 7, x: 30, hitbox: 2f);
        var w = TestData.World(player: TestData.Player(melee: false, synced: true, targetId: 7), fates: [fate], enemies: [enemy]);
        var sut = Sut();
        sut.Tick(w, Ctx(fate)); // SetCombat
        var go = Assert.IsType<GoTo>(sut.Tick(w, Ctx(fate)).Intent);
        Assert.Equal(8f + 2f, go.Tolerance);
    }

    [Fact] // B9: no navigation during a boss fight
    public void B9_no_navigation_during_boss_combat()
    {
        var fate = TestData.Fate(kind: FateKind.Boss, x: 0, z: 0, radius: 60);
        var boss = TestData.Enemy(id: 7, x: 30);
        var w = TestData.World(player: TestData.Player(synced: true, inCombat: true, targetId: 7), fates: [fate], enemies: [boss]);
        var sut = Sut();
        sut.Tick(w, Ctx(fate)); // SetCombat
        Assert.IsNotType<GoTo>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // B6: escort follow hysteresis
    public void B6_escort_follow_hysteresis()
    {
        var follow = new EscortFollow(start: 5f, stop: 2.5f);
        Assert.IsType<NoAction>(follow.Decide(new Vector3(4, 0, 0), Vector3.Zero));
        Assert.IsType<GoTo>(follow.Decide(new Vector3(6, 0, 0), Vector3.Zero));
        Assert.IsType<GoTo>(follow.Decide(new Vector3(4, 0, 0), Vector3.Zero));   // still closing
        Assert.IsType<NoAction>(follow.Decide(new Vector3(2, 0, 0), Vector3.Zero)); // reached stop distance
    }

    [Fact] // escort: fight when fate enemies exist, follow otherwise
    public void Escort_fights_then_follows()
    {
        var fate = TestData.Fate(kind: FateKind.Escort, x: 0, z: 0, radius: 60);
        var npc = TestData.Thing(id: 42, x: 10, kind: InteractableKind.ObjectiveNpc);
        var sut = new EscortBehavior(new EngageBehavior(new EngageConfig()));

        var fighting = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            enemies: [TestData.Enemy(x: 3)], interactables: [npc]);
        Assert.IsType<SetCombat>(sut.Tick(fighting, Ctx(fate)).Intent);

        var calm = TestData.World(player: TestData.Player(synced: true), fates: [fate], interactables: [npc]);
        Assert.IsType<GoTo>(sut.Tick(calm, Ctx(fate)).Intent);
    }

    [Fact] // C5: elevation alone never reads as a knockback out of the ring
    public void C5_elevation_inside_ring_is_not_a_knockback()
    {
        var fate = TestData.Fate(x: 0, z: 0, radius: 20);
        var w = TestData.World(player: TestData.Player(x: 5, y: 30, z: 0, synced: true), fates: [fate],
            enemies: [TestData.Enemy(x: 5, y: 30, fateId: 1)]);
        Assert.IsType<SetCombat>(Sut().Tick(w, Ctx(fate)).Intent);
    }
}
