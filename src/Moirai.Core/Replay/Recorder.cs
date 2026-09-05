using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Model;

namespace Moirai.Core.Replay;

// Keeps the newest frames of a run. It sits between the Director and its two injected
// services, so every random draw and landing answer the planner consumed is in the frame (§12).
public sealed class Recorder
{
    public const int DefaultCapacity = 3600; // a minute at the game's frame rate

    private readonly RunSettings _settings;
    private readonly string _version;
    private readonly int _capacity;
    private readonly Queue<Frame> _frames = new();
    private readonly List<double> _draws = [];
    private readonly List<LandingQuery> _landings = [];

    public Recorder(RunSettings settings, string version, IRandomSource random, ILandingResolver landing, int capacity = DefaultCapacity)
    {
        _settings = settings;
        _version = version;
        _capacity = capacity;
        Random = new TapRandom(random, _draws);
        Landing = new TapLanding(landing, _landings);
    }

    // Hand these to the Director in place of the real services
    public IRandomSource Random { get; }
    public ILandingResolver Landing { get; }

    public int Count => _frames.Count;

    public DirectorOutput Tick(Director director, WorldSnapshot world, bool zoneFlightAllowed)
    {
        _draws.Clear();
        _landings.Clear();
        var output = director.Tick(world);
        _frames.Enqueue(new Frame(world, zoneFlightAllowed, [.. _draws], [.. _landings], output.Status, output.Intent.ToString() ?? ""));
        while (_frames.Count > _capacity)
            _frames.Dequeue();
        return output;
    }

    public Recording Snapshot() => new(_version, _settings, [.. _frames]);

    private sealed class TapRandom(IRandomSource inner, List<double> log) : IRandomSource
    {
        public double NextDouble()
        {
            var value = inner.NextDouble();
            log.Add(value);
            return value;
        }
    }

    private sealed class TapLanding(ILandingResolver inner, List<LandingQuery> log) : ILandingResolver
    {
        public Vector3? ResolveFloor(Vector3 near)
        {
            var floor = inner.ResolveFloor(near);
            log.Add(new LandingQuery(near, floor));
            return floor;
        }
    }
}
