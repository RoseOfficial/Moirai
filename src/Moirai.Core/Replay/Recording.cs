using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Replay;

// One landing-resolver call the planner made during a tick, with the answer it got
public sealed record LandingQuery(Vector3 Near, Vector3? Floor);

// One planner tick: every input it consumed and what it produced. The random draws and landing
// answers are the planner's only inputs besides the snapshot, so a frame replays exactly (§12).
public sealed record Frame(
    WorldSnapshot World,
    bool ZoneFlightAllowed,
    IReadOnlyList<double> RandomDraws,
    IReadOnlyList<LandingQuery> Landings,
    string Status,
    string Intent);

public sealed record Recording(
    string Version,
    RunSettings Settings,
    IReadOnlyList<Frame> Frames);
