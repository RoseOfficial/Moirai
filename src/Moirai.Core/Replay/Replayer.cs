using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Modules;

namespace Moirai.Core.Replay;

public sealed class ReplayException(string message) : Exception(message);

public sealed record ReplayResult(int Index, string RecordedStatus, string ReplayedStatus, string RecordedIntent, string ReplayedIntent)
{
    public bool Matches => RecordedStatus == ReplayedStatus && RecordedIntent == ReplayedIntent;
}

// Runs a recording through a fresh Director built from its own settings, feeding back the
// inputs each frame carried, and lines the outputs up against what was recorded (§12)
public static class Replayer
{
    public static IReadOnlyList<ReplayResult> Run(Recording recording, IFarmModule? module = null)
    {
        var feed = new FrameFeed();
        var director = DirectorFactory.Create(recording.Settings, module, feed, feed, _ => feed.ZoneFlightAllowed);
        director.Start();

        var results = new List<ReplayResult>(recording.Frames.Count);
        for (var i = 0; i < recording.Frames.Count; i++)
        {
            var frame = recording.Frames[i];
            feed.Load(i, frame);
            try
            {
                var output = director.Tick(frame.World);
                results.Add(new ReplayResult(i, frame.Status, output.Status, frame.Intent, output.Intent.ToString() ?? ""));
            }
            catch (ReplayException e)
            {
                // the planner asked for an input the frame does not carry: it has already taken a
                // different path than the recorded run, and nothing after this frame is comparable
                results.Add(new ReplayResult(i, frame.Status, $"replay stopped: {e.Message}", frame.Intent, ""));
                break;
            }
        }
        return results;
    }

    public static ReplayResult? FirstDivergence(IReadOnlyList<ReplayResult> results)
        => results.FirstOrDefault(r => !r.Matches);

    // Feeds each frame's recorded draws and landing answers back in order. Anything the planner
    // asks for beyond them means the replay has already diverged, and that is an error.
    private sealed class FrameFeed : IRandomSource, ILandingResolver
    {
        private int _index;
        private Frame? _frame;
        private int _draw;
        private int _landing;

        public bool ZoneFlightAllowed => Current.ZoneFlightAllowed;

        private Frame Current => _frame ?? throw new ReplayException("no frame loaded");

        public void Load(int index, Frame frame)
        {
            _index = index;
            _frame = frame;
            _draw = 0;
            _landing = 0;
        }

        public double NextDouble()
        {
            if (_draw >= Current.RandomDraws.Count)
                throw new ReplayException($"frame {_index}: the planner asked for a random draw the recording does not carry");
            return Current.RandomDraws[_draw++];
        }

        public Vector3? ResolveFloor(Vector3 near)
        {
            if (_landing >= Current.Landings.Count)
                throw new ReplayException($"frame {_index}: the planner asked for a landing point the recording does not carry");
            var query = Current.Landings[_landing++];
            if (Vector3.Distance(query.Near, near) > 0.01f)
                throw new ReplayException($"frame {_index}: the planner asked for a landing point near {near}; the recording has {query.Near}");
            return query.Floor;
        }
    }
}
