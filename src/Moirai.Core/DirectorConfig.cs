namespace Moirai.Core;

public sealed class DirectorConfig
{
    public int DeathCap { get; init; } = 3;
    public int IdleHoldMs { get; init; } = 2000;
    public float WedgedRadius { get; init; } = 10f; // D10: exhaustions closer together than this are "in place"
    public int WedgedStopAfter { get; init; } = 3;  // D10: that many in a row stops the run
    public long DependencyGraceMs { get; init; } = 60_000; // D8: a plugin missing this long stops the run
}
