namespace Moirai.Core.Intents;

public enum StopReason
{
    UserRequested,
    StuckExhausted,
    DeathCapReached,
    DependencyLost,
    DataMissing,
    SessionComplete,
}
