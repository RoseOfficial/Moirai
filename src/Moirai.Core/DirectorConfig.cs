namespace Moirai.Core;

public sealed class DirectorConfig
{
    public int DeathCap { get; init; } = 3;
    public int IdleHoldMs { get; init; } = 2000;
    public float WedgedRadius { get; init; } = 10f; // D10: exhaustions closer together than this are "in place"
    public int WedgedStopAfter { get; init; } = 3;  // D10: that many in a row stops the run
    public long DependencyGraceMs { get; init; } = 60_000; // D8: a plugin missing this long stops the run
    public bool Sprint { get; init; } = true;              // C20: sprint on long walks
    public float SprintOverYalms { get; init; } = 20f;     // C20: a walk longer than this asks for sprint
    public long DutyResumeGraceMs { get; init; } = 5_000;  // D11: back from a duty this long before the session picks up
}
