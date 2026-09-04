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
}
