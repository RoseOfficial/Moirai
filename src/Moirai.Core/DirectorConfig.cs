namespace Moirai.Core;

public sealed class DirectorConfig
{
    public int DeathCap { get; init; } = 3;
    public int IdleHoldMs { get; init; } = 2000;
}
