using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Tests;

public class StrayAggroClearTests
{
    private static StrayAggroClear Sut() => new(new EngageConfig());

    [Fact] // D3: a non-fate mob on us is fought where we stand: rotation defensive, then targeted, then held
    public void D3_stray_on_foot_is_engaged_defensively()
    {
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, z: 0, attacksMe: true);
        var sut = Sut();
        var w = TestData.World(enemies: [stray]);

        var first = sut.Tick(w, currentFateId: null)!;
        var on = Assert.IsType<SetCombat>(first.Intent);
        Assert.True(on.Enabled);
        Assert.Equal(CombatMode.Defensive, on.Mode);
        Assert.True(sut.Fighting);

        Assert.Equal(9uL, Assert.IsType<Engage>(sut.Tick(w, null)!.Intent).TargetId);

        // in range and on target: the defensive mode is re-asserted each tick, in case the backend dropped it
        var targeted = TestData.World(player: TestData.Player(targetId: 9), enemies: [stray]);
        var held = Assert.IsType<SetCombat>(sut.Tick(targeted, null)!.Intent);
        Assert.True(held.Enabled);
        Assert.Equal(CombatMode.Defensive, held.Mode);
    }

    [Fact] // D3: a stray out of reach is closed on at the job's engage distance, on foot
    public void D3_closes_distance_to_a_far_stray()
    {
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 30, z: 0, hitbox: 2f, attacksMe: true);
        var sut = Sut();
        var w = TestData.World(player: TestData.Player(melee: false, targetId: 9), enemies: [stray]);
        sut.Tick(w, null); // defensive on
        var go = Assert.IsType<GoTo>(sut.Tick(w, null)!.Intent);
        Assert.Equal(stray.Position, go.Destination);
        Assert.Equal(8f + 2f, go.Tolerance);
        Assert.False(go.Fly);
    }

    [Fact] // D3: the nearest stray first, then stick to it while it is still on us
    public void D3_nearest_stray_then_sticky()
    {
        var near = TestData.Enemy(id: 1, fateId: 0, x: 2, z: 0, attacksMe: true);
        var far = TestData.Enemy(id: 2, fateId: 0, x: 10, z: 0, attacksMe: true);
        var sut = Sut();
        var w = TestData.World(enemies: [near, far]);
        sut.Tick(w, null); // defensive on
        Assert.Equal(1uL, Assert.IsType<Engage>(sut.Tick(w, null)!.Intent).TargetId);

        // they trade places: the held one is kept, so we close on it rather than swap
        var swapped = TestData.World(player: TestData.Player(targetId: 1),
            enemies: [near with { Position = new Vector3(10, 0, 0) }, far with { Position = new Vector3(2, 0, 0) }]);
        var go = Assert.IsType<GoTo>(sut.Tick(swapped, null)!.Intent);
        Assert.Equal(new Vector3(10, 0, 0), go.Destination);
    }

    [Fact] // D3 mounted variant: nothing is fought from the saddle; the leg carries on and the leash ends it
    public void D3_mounted_rides_stray_aggro_out()
    {
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var w = TestData.World(player: TestData.Player(mounted: true), enemies: [stray]);
        var sut = Sut();
        Assert.Null(sut.Tick(w, null));
        Assert.False(sut.Fighting);
    }

    [Fact] // D3: our own fate's mob is never a stray, even outside its ring: we carry it into the ring
    public void D3_own_fate_enemy_is_not_a_stray()
    {
        var ours = TestData.Enemy(id: 9, fateId: 1, x: 1, attacksMe: true);
        Assert.Null(Sut().Tick(TestData.World(enemies: [ours]), currentFateId: 1));
    }

    [Fact] // D3: between fates anything on us is a stray, another fate's mob included
    public void D3_any_attacker_is_a_stray_between_fates()
    {
        var foreign = TestData.Enemy(id: 9, fateId: 2, x: 1, attacksMe: true);
        Assert.IsType<SetCombat>(Sut().Tick(TestData.World(enemies: [foreign]), null)!.Intent);
    }

    [Fact] // D3: another fate's mob on us inside our own fate is a stray
    public void D3_foreign_fate_enemy_is_a_stray_inside_our_fate()
        => Assert.True(StrayAggroClear.IsStray(TestData.Enemy(fateId: 2, attacksMe: true), currentFateId: 1));

    [Fact] // D3: a mob that is not on us is never engaged, however close
    public void D3_ignores_mobs_not_attacking_us()
    {
        var bystander = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: false);
        Assert.Null(Sut().Tick(TestData.World(enemies: [bystander]), null));
    }

    [Fact] // D3: a dead stray is not a target
    public void D3_dead_stray_is_ignored()
    {
        var corpse = TestData.Enemy(id: 9, fateId: 0, x: 1, alive: false, attacksMe: true);
        Assert.Null(Sut().Tick(TestData.World(enemies: [corpse]), null));
    }

    [Fact] // D3: once nothing is on us the rotation stands down, exactly once, even while the combat flag lingers
    public void D3_stands_down_once_when_clear()
    {
        var stray = TestData.Enemy(id: 9, fateId: 0, x: 1, attacksMe: true);
        var sut = Sut();
        sut.Tick(TestData.World(enemies: [stray]), null);

        var clear = TestData.World(player: TestData.Player(inCombat: true));
        var down = sut.Tick(clear, null)!;
        var off = Assert.IsType<SetCombat>(down.Intent);
        Assert.False(off.Enabled);
        Assert.Equal(BehaviorStatus.Done, down.Status);
        Assert.False(sut.Fighting);

        Assert.Null(sut.Tick(clear, null));
    }
}
