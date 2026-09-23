using Moirai.Core.Model;

namespace Moirai.Core.Session;

public enum FateOutcome { Completed, Failed, Abandoned }

public sealed class SessionLedger
{
    private long? _lastEpoch;

    public int Completed { get; private set; }
    public int Failed { get; private set; }
    public int Abandoned { get; private set; }
    public int Deaths { get; private set; }

    // The session clock: the time between observed ticks, a pause excluded
    public long ElapsedSeconds { get; private set; }
    public double CompletedPerHour => ElapsedSeconds > 0 ? Completed * 3600.0 / ElapsedSeconds : 0;

    public void Observe(long nowEpoch)
    {
        if (_lastEpoch is { } last)
            ElapsedSeconds += Math.Max(0, nowEpoch - last);
        _lastEpoch = nowEpoch;
    }

    // §11: a pause is not session time; the first tick after it starts the clock again
    public void Pause() => _lastEpoch = null;

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
