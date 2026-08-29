namespace Moirai.Core.Intents;

public enum StopReason
{
    UserRequested,
    AllYokaiCapped,
    WatchMissing,
    StuckExhausted,
    DeathCapReached,
    DependencyLost,
    DataMissing,
    SessionComplete,
}
