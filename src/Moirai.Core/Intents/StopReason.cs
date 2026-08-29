namespace Moirai.Core.Intents;

public enum StopReason
{
    UserRequested,
    AllYokaiCapped,
    WatchMissing,
    MinionsMissing,
    StuckExhausted,
    DeathCapReached,
    DependencyLost,
    DataMissing,
    SessionComplete,
}
