using Moirai.Core.Model;

namespace Moirai.Core.Session;

public enum FateOutcome { Completed, Failed, Abandoned }

public sealed class SessionLedger
{
    public int Completed { get; private set; }
    public int Failed { get; private set; }
    public int Abandoned { get; private set; }
    public int Deaths { get; private set; }

    // D2: outcome derives from the observed end phase, nothing else
    public static FateOutcome OutcomeFrom(FatePhase endPhase) => endPhase switch
    {
        FatePhase.Ended => FateOutcome.Completed,
        FatePhase.Failed => FateOutcome.Failed,
        _ => FateOutcome.Abandoned,
    };

    public void Record(FateOutcome outcome)
    {
        switch (outcome)
        {
            case FateOutcome.Completed: Completed++; break;
            case FateOutcome.Failed: Failed++; break;
            case FateOutcome.Abandoned: Abandoned++; break;
        }
    }

    public void RecordDeath() => Deaths++;
}
