using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Tests;

public class CollectAndNpcStartTests
{
    private static BehaviorContext Ctx(FateSnapshot fate)
        => new(fate, true, new FixedRandom(0.5), new FlatGround());

    private static CollectBehavior Collect() => new(new EngageBehavior(new EngageConfig()));

    private static FateSnapshot CollectFate(int progress = 50)
        => TestData.Fate(id: 1, kind: FateKind.Collect, x: 0, z: 0, radius: 60, progress: progress, eventItemId: 900);

    [Fact] // B1: at 7 items, hand in — combat off first (B2)
    public void B1_B2_hands_in_at_full_credit_with_combat_off()
    {
        var fate = CollectFate();
        var npc = TestData.Thing(id: 42, x: 3, z: 0, kind: InteractableKind.ObjectiveNpc);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            interactables: [npc], items: new Dictionary<uint, int> { [900] = 7 });
        var sut = Collect();
        var off = Assert.IsType<SetCombat>(sut.Tick(w, Ctx(fate)).Intent);
        Assert.False(off.Enabled);
        Assert.IsType<InteractWith>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // B1: partial hand-in when the fate hits 100 with items short of 7
    public void B1_partial_hand_in_at_completion()
    {
        var fate = CollectFate(progress: 100);
        var npc = TestData.Thing(id: 42, x: 3, z: 0, kind: InteractableKind.ObjectiveNpc);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            interactables: [npc], items: new Dictionary<uint, int> { [900] = 3 });
        var sut = Collect();
        sut.Tick(w, Ctx(fate)); // combat off
        Assert.IsType<InteractWith>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // pickup: collect ground items when short of credit
    public void Picks_up_fate_collectables()
    {
        var fate = CollectFate();
        var item = TestData.Thing(id: 50, x: 2, z: 0, kind: InteractableKind.Collectable);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            interactables: [item], items: new Dictionary<uint, int> { [900] = 2 });
        var sut = Collect();
        sut.Tick(w, Ctx(fate)); // combat off for pickup (B2)
        Assert.IsType<InteractWith>(sut.Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // B3: event item id may not be populated yet — fight, don't throw
    public void B3_tolerates_missing_event_item()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.Collect, x: 0, z: 0, radius: 60, eventItemId: 0);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            enemies: [TestData.Enemy(x: 3, z: 0)]);
        var step = Collect().Tick(w, Ctx(fate));
        Assert.IsType<SetCombat>(step.Intent); // delegated to the fight loop
    }

    [Fact]
    public void Collect_done_when_complete_and_empty_handed()
    {
        var fate = CollectFate(progress: 100);
        var w = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            items: new Dictionary<uint, int> { [900] = 0 });
        Assert.Equal(BehaviorStatus.Done, Collect().Tick(w, Ctx(fate)).Status);
    }

    [Fact] // B5: starter NPC may carry fate id 0 — fall back to any starter inside the ring
    public void B5_falls_back_to_starter_inside_ring()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 60,
            startTimeEpoch: 0, progress: 0);
        var starter = TestData.Thing(id: 60, x: 10, z: 0, fateId: 0, kind: InteractableKind.StarterNpc);
        var w = TestData.World(player: TestData.Player(x: 10, z: 0), fates: [fate], interactables: [starter]);
        var sut = new NpcStartBehavior();
        Assert.IsType<InteractWith>(sut.Tick(w, Ctx(fate)).Intent); // B4: executor confirms only the fate-start dialog
    }

    [Fact]
    public void NpcStart_dismounts_before_interacting()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 60,
            startTimeEpoch: 0, progress: 0);
        var starter = TestData.Thing(id: 60, x: 1, z: 0, fateId: 1, kind: InteractableKind.StarterNpc);
        var w = TestData.World(player: TestData.Player(x: 0, z: 0, mounted: true), fates: [fate], interactables: [starter]);
        Assert.IsType<Dismount>(new NpcStartBehavior().Tick(w, Ctx(fate)).Intent);
    }

    [Fact]
    public void NpcStart_done_once_fate_opens()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, startTimeEpoch: 5_000, progress: 0);
        var w = TestData.World(fates: [fate]);
        Assert.Equal(BehaviorStatus.Done, new NpcStartBehavior().Tick(w, Ctx(fate)).Status);
    }
}
