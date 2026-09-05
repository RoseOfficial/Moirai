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
    AllYokaiCapped,   // E5: every listed yokai is at the cap
    MinionsMissing,   // E5: nothing farmable until minions are bought
}
