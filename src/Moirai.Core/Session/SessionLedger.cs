using Moirai.Core.Model;

namespace Moirai.Core.Session;

public enum FateOutcome { Completed, Failed, Abandoned }

public sealed class SessionLedger
{
    private long? _startedEpoch;
    private long _nowEpoch;

    public int Completed { get; private set; }
    public int Failed { get; private set; }
    public int Abandoned { get; private set; }
    public int Deaths { get; private set; }

    // The session clock, from the first observed tick to the latest
    public long ElapsedSeconds => _startedEpoch is { } started ? Math.Max(0, _nowEpoch - started) : 0;
    public double CompletedPerHour => ElapsedSeconds > 0 ? Completed * 3600.0 / ElapsedSeconds : 0;

    public void Observe(long nowEpoch)
    {
        _startedEpoch ??= nowEpoch;
        _nowEpoch = nowEpoch;
    }

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
