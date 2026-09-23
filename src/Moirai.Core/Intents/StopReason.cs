namespace Moirai.Core.Intents;

public enum StopReason
{
    UserRequested,
    StuckExhausted,
    DeathCapReached,
    OutOfGreens,
    DependencyLost,
    ZonesUnreachable, // G5: none of the listed zones could be reached
    AllYokaiCapped,   // E5: every listed yokai is at the cap
    MinionsMissing,   // E5: nothing farmable until minions are bought
    InternalError,    // the plugin shell caught an exception from a tick and stood everything down
    GearBroken,       // R7: a piece of gear broke and no repair is coming
}
