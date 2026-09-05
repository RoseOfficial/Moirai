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

    [Fact] // B2: combat comes back for the next fight goal after a pickup turned it off
    public void B2_combat_reengaged_for_the_next_fight_goal()
    {
        var fate = CollectFate();
        var enemy = TestData.Enemy(id: 7, fateId: 1, x: 3, z: 0);
        var fighting = TestData.World(player: TestData.Player(synced: true), fates: [fate], enemies: [enemy]);
        var sut = Collect();
        Assert.True(Assert.IsType<SetCombat>(sut.Tick(fighting, Ctx(fate)).Intent).Enabled);

        var item = TestData.Thing(id: 50, x: 1, z: 0, kind: InteractableKind.Collectable);
        var pickup = TestData.World(player: TestData.Player(synced: true), fates: [fate], enemies: [enemy], interactables: [item]);
        Assert.False(Assert.IsType<SetCombat>(sut.Tick(pickup, Ctx(fate)).Intent).Enabled);

        Assert.True(Assert.IsType<SetCombat>(sut.Tick(fighting, Ctx(fate)).Intent).Enabled);
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

    [Fact] // B11: a collect fate opens at the same NPC it hands in to — use it when no starter is tagged
    public void B11_starts_collect_fate_at_its_objective_npc()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 60,
            startTimeEpoch: 0, progress: 0, eventItemId: 900);
        var npc = TestData.Thing(id: 42, x: 1, z: 0, fateId: 1, kind: InteractableKind.ObjectiveNpc);
        var w = TestData.World(player: TestData.Player(x: 0, z: 0), fates: [fate], interactables: [npc]);
        Assert.IsType<InteractWith>(new NpcStartBehavior().Tick(w, Ctx(fate)).Intent);
    }

    [Fact] // B5: the ring-proximity fallback is horizontal, so a starter on a rise still matches
    public void B5_ring_fallback_matches_starter_on_a_rise()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 20,
            startTimeEpoch: 0, progress: 0);
        var starter = TestData.Thing(id: 60, x: 10, y: 25, z: 0, fateId: 0, kind: InteractableKind.StarterNpc);
        var w = TestData.World(player: TestData.Player(x: 10, y: 25, z: 0), fates: [fate], interactables: [starter]);
        Assert.IsType<InteractWith>(new NpcStartBehavior().Tick(w, Ctx(fate)).Intent);
    }

    private static FateSnapshot Unopened()
        => TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 60,
            phase: FatePhase.Preparing, startTimeEpoch: 0, progress: 0);

    private static InteractableSnapshot Starter() => TestData.Thing(id: 60, x: 1, z: 0, fateId: 1, kind: InteractableKind.StarterNpc);

    private static WorldSnapshot AtStarter(long ms, DialogKind dialog = DialogKind.None, FateSnapshot? fate = null)
        => TestData.World(nowMs: ms, player: TestData.Player(x: 0, z: 0, occupied: dialog != DialogKind.None),
            fates: [fate ?? Unopened()], interactables: [Starter()], dialog: dialog);

    [Fact] // B4: the fate-start prompt is confirmed only after our own interact with the starter
    public void B4_confirms_the_fate_start_prompt_after_its_own_interact()
    {
        var sut = new NpcStartBehavior();
        Assert.IsType<InteractWith>(sut.Tick(AtStarter(0), Ctx(Unopened())).Intent);
        var step = sut.Tick(AtStarter(500, DialogKind.YesNo), Ctx(Unopened()));
        Assert.IsType<ConfirmDialog>(step.Intent);
        Assert.Equal("accepting fate start", step.Note);
    }

    [Fact] // B4: a prompt that was open before we interacted is not ours; leave it alone
    public void B4_leaves_a_prompt_alone_before_any_interact()
    {
        var step = new NpcStartBehavior().Tick(AtStarter(0, DialogKind.YesNo), Ctx(Unopened()));
        Assert.IsType<Hold>(step.Intent);
    }

    [Fact] // B4: the Talk window before the prompt is TextAdvance's; wait it out rather than interact again
    public void B4_talk_window_is_waited_out()
    {
        var sut = new NpcStartBehavior();
        sut.Tick(AtStarter(0), Ctx(Unopened()));
        var step = sut.Tick(AtStarter(300, DialogKind.Talk), Ctx(Unopened()));
        Assert.IsType<Hold>(step.Intent);
        Assert.Equal("advancing dialogue", step.Note);
    }

    [Fact] // B4: the fate opens a few seconds after the prompt; wait for it instead of interacting again
    public void B4_waits_for_the_fate_to_open_after_confirming()
    {
        var sut = new NpcStartBehavior();
        sut.Tick(AtStarter(0), Ctx(Unopened()));
        Assert.IsType<ConfirmDialog>(sut.Tick(AtStarter(500, DialogKind.YesNo), Ctx(Unopened())).Intent);

        var waiting = sut.Tick(AtStarter(2_500), Ctx(Unopened()));
        Assert.IsType<Hold>(waiting.Intent);
        Assert.Equal("waiting for the fate to open", waiting.Note);

        Assert.IsType<InteractWith>(sut.Tick(AtStarter(11_000), Ctx(Unopened())).Intent); // never took: ask again
    }

    [Fact] // B1: while the hand-in dialogue runs, wait rather than interact on every throttle
    public void B1_hand_in_waits_while_a_dialog_is_open()
    {
        var fate = CollectFate();
        var npc = TestData.Thing(id: 42, x: 3, z: 0, kind: InteractableKind.ObjectiveNpc);
        var sut = Collect();
        var ready = TestData.World(player: TestData.Player(synced: true), fates: [fate],
            interactables: [npc], items: new Dictionary<uint, int> { [900] = 7 });
        sut.Tick(ready, Ctx(fate)); // combat off
        Assert.IsType<InteractWith>(sut.Tick(ready, Ctx(fate)).Intent);

        var handingIn = TestData.World(player: TestData.Player(synced: true, occupied: true), fates: [fate],
            interactables: [npc], items: new Dictionary<uint, int> { [900] = 7 }, dialog: DialogKind.Request);
        var step = sut.Tick(handingIn, Ctx(fate));
        Assert.IsType<Hold>(step.Intent);
        Assert.Equal("handing in", step.Note);
    }

    [Fact] // B11: a fate still preparing has not opened, whatever its start-time field says
    public void NpcStart_keeps_starting_while_fate_is_preparing()
    {
        var fate = TestData.Fate(id: 1, kind: FateKind.NpcStart, x: 0, z: 0, radius: 60,
            phase: FatePhase.Preparing, startTimeEpoch: 5_000, progress: 0);
        var starter = TestData.Thing(id: 60, x: 1, z: 0, fateId: 1, kind: InteractableKind.StarterNpc);
        var w = TestData.World(player: TestData.Player(x: 0, z: 0), fates: [fate], interactables: [starter]);
        Assert.IsType<InteractWith>(new NpcStartBehavior().Tick(w, Ctx(fate)).Intent);
    }
}
