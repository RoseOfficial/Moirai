namespace Moirai.Core.Intents;

public enum StopReason
{
    UserRequested,
    StuckExhausted,
    DeathCapReached,
    OutOfGreens,
    DependencyLost,
    DataMissing,
    SessionComplete,
    ZonesUnreachable, // G5: none of the listed zones could be reached
}
