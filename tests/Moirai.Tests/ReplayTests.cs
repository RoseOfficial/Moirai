using System.Numerics;
using Moirai.Core;
using Moirai.Core.Behaviors;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;
using Moirai.Core.Replay;

namespace Moirai.Tests;

public class ReplayTests
{
    private static RunSettings Settings() => new(
        new SelectionConfig
        {
            Blacklist = new HashSet<uint> { 218 },
            Priority = [SelectionCriterion.Distance, SelectionCriterion.Progress],
            BonusOnly = true,
        },
        new MovementConfig { MountLegThreshold = 25f },
        new EngageConfig { RangedRange = 9f },
        new CompanionConfig { Enabled = true, GreensItemId = 4868, StanceActionId = 7 },
        new DirectorConfig { DeathCap = 2 });

    private static WorldSnapshot RichWorld() => TestData.World(now: 123, nowMs: 456, territory: 140,
        player: TestData.Player(x: 1.5f, y: -2, z: 3, level: 45, mounted: true, targetId: 77,
            companionSummoned: true, companionTimeLeft: 900, companionStance: 7),
        fates: [TestData.Fate(id: 9, x: 10, z: 20, kind: FateKind.Collect, eventItemId: 900, specialBoss: true)],
        aetherytes: [new Aetheryte(3, new Vector3(5, 6, 7))],
        enemies: [TestData.Enemy(id: 1000, fateId: 9, attacksMe: true, maxHp: 5000)],
        interactables: [TestData.Thing(id: 2000, fateId: 9, kind: InteractableKind.ObjectiveNpc)],
        items: new Dictionary<uint, int> { [900] = 3, [4868] = 10 },
        navmeshReady: false, dialog: DialogKind.YesNo, combatReady: false);

    [Fact] // a recording survives the file format unchanged
    public void Recording_round_trips_through_the_file_format()
    {
        var frame = new Frame(RichWorld(), ZoneFlightAllowed: false, RandomDraws: [0.25, 0.75],
            Landings: [new LandingQuery(new Vector3(1, 2, 3), new Vector3(1, 0, 3)), new LandingQuery(new Vector3(4, 5, 6), null)],
            Status: "moving", Intent: "GoTo");
        var recording = new Recording("0.3.10", Settings(), [frame, frame with { Status = "arrived" }]);

        using var stream = new MemoryStream();
        RecordingFile.Write(recording, stream);
        stream.Position = 0;
        var back = RecordingFile.Read(stream);

        Assert.Equal(RecordingFile.ToJson(recording), RecordingFile.ToJson(back));
        Assert.Equal(2, back.Frames.Count);
        Assert.Equal(218u, back.Settings.Selection.Blacklist.Single());
        Assert.Equal([SelectionCriterion.Distance, SelectionCriterion.Progress], back.Settings.Selection.Priority);
        Assert.Equal(DialogKind.YesNo, back.Frames[0].World.Dialog);
        Assert.Equal(new Vector3(1, 0, 3), back.Frames[0].Landings[0].Floor);
        Assert.Null(back.Frames[0].Landings[1].Floor);
        Assert.Equal(3, back.Frames[0].World.CountOf(900));
    }

    private static RunSettings Defaults(MovementConfig? movement = null) => new(
        new SelectionConfig(), movement ?? new MovementConfig(), new EngageConfig(), new CompanionConfig(), new DirectorConfig());

    // A wedged leg driven through the ladder to exhaustion, then a death: random draws (the dropoff,
    // the nudge direction), landing queries, and several state machines all take part
    private static Recording RecordWedgedRun(int capacity = 10_000)
    {
        var settings = Defaults();
        var recorder = new Recorder(settings, "test", new FixedRandom(0.5, 0.5, 0.1, 0.9), new FlatGround(), capacity);
        var director = DirectorFactory.Create(settings, new SingleZoneModule(), recorder.Random, recorder.Landing);
        director.Start();
        var fates = new[] { TestData.Fate(id: 1, x: 100, z: 0, radius: 20), TestData.Fate(id: 2, x: -900, z: 0, radius: 20) };
        for (long ms = 0; ms <= 25_000; ms += 100)
            recorder.Tick(director, TestData.World(nowMs: ms, player: TestData.Player(x: 300, z: 0, canMount: false), fates: fates), zoneFlightAllowed: true);
        recorder.Tick(director, TestData.World(nowMs: 25_100, player: TestData.Player(x: 300, z: 0, dead: true), fates: fates), zoneFlightAllowed: true);
        return recorder.Snapshot();
    }

    [Fact] // §12: a recorded run replays to the same output on every tick
    public void Record_then_replay_reproduces_every_output()
    {
        var recording = RecordWedgedRun();
        Assert.Contains(recording.Frames, f => f.Status == "recovery: nudging sideways");
        Assert.Contains(recording.Frames, f => f.Status.StartsWith("recovery exhausted"));
        Assert.Contains(recording.Frames, f => f.Landings.Count > 0);

        var results = Replayer.Run(recording);
        Assert.Equal(recording.Frames.Count, results.Count);
        Assert.Null(Replayer.FirstDivergence(results));
    }

    [Fact] // §12: the replay honors the recording's settings, so a changed one shows up as a divergence
    public void Replay_with_different_settings_diverges()
    {
        var recording = RecordWedgedRun();
        var slower = recording with { Settings = Defaults(new MovementConfig { StuckWindowMs = 4000 }) };
        var first = Replayer.FirstDivergence(Replayer.Run(slower));
        Assert.NotNull(first);
        Assert.NotEqual(first.RecordedStatus, first.ReplayedStatus);
    }

    [Fact] // §12: an input the recording does not carry is reported as the divergence, never a silent default
    public void Missing_random_draw_fails_loudly()
    {
        var recording = RecordWedgedRun();
        var stripped = recording with { Frames = recording.Frames.Select(f => f with { RandomDraws = [] }).ToList() };
        var results = Replayer.Run(stripped);
        var first = Replayer.FirstDivergence(results);
        Assert.NotNull(first);
        Assert.Contains("random draw", first.ReplayedStatus);
        Assert.Equal(first.Index, results[^1].Index); // nothing after it is comparable, so the replay stops there
    }

    [Fact] // §8/§12: a recording carries the yokai roster and settings, so a replay rebuilds the same module
    public void Recording_round_trips_yokai_settings()
    {
        var settings = Defaults() with
        {
            Yokai = new YokaiConfig
            {
                Enabled = true,
                Roster = [new Yokai(200, "Jibanyan", 15168, [148, 135, 141], MinionItemId: 15195)],
                Priority = [200],
                WatchItemId = 15222,
                LegendaryCap = 7,
            },
        };
        var world = TestData.World(player: TestData.Player(activeMinionId: 200, watchEquipped: true, watchOwned: true),
            ownedMinions: new HashSet<uint> { 200, 201 });
        var recording = new Recording("0.4.0", settings, [new Frame(world, true, [], [], "idle", "NoAction")]);

        var back = RecordingFile.FromJson(RecordingFile.ToJson(recording));
        Assert.Equal(RecordingFile.ToJson(recording), RecordingFile.ToJson(back));
        Assert.Equal(7, back.Settings.Yokai!.LegendaryCap);
        Assert.Equal([148, 135, 141], back.Settings.Yokai.Roster[0].TerritoryIds);
        Assert.Contains(201u, back.Frames[0].World.OwnedMinions!);
        Assert.IsType<YokaiModule>(DirectorFactory.CreateModule(back.Settings));
    }

    [Fact] // the debug report's timeline: one line per status change, newest kept
    public void Status_timeline_records_changes_only_and_keeps_the_newest()
    {
        var timeline = new StatusTimeline(capacity: 3);
        timeline.Observe(100, "selected fate 1");
        timeline.Observe(101, "mounting");
        timeline.Observe(102, "mounting");
        timeline.Observe(140, "moving");
        timeline.Observe(150, "recovery: re-path");

        Assert.Equal([(101, "mounting"), (140, "moving"), (150, "recovery: re-path")], timeline.Entries);
    }

    [Fact] // §12: every recording checked into the Recordings folder must replay without divergence
    public void Checked_in_recordings_replay_without_divergence()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Recordings");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, "*" + RecordingFile.Extension))
        {
            using var stream = File.OpenRead(file);
            var results = Replayer.Run(RecordingFile.Read(stream));
            var first = Replayer.FirstDivergence(results);
            Assert.True(first is null, $"{Path.GetFileName(file)} diverged at frame {first?.Index}: recorded '{first?.RecordedStatus}', replayed '{first?.ReplayedStatus}'");
        }
    }

    [Fact] // the plugin's buffer is bounded: only the newest frames survive
    public void Recorder_keeps_only_the_last_capacity_frames()
    {
        var recording = RecordWedgedRun(capacity: 50);
        Assert.Equal(50, recording.Frames.Count);
        Assert.Equal(25_100, recording.Frames[^1].World.NowMs);
    }
}
