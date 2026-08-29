namespace Moirai.Core.Planning;

public enum RecoveryRung { RePath, RerollDestination, VerticalEscape, ReturnToAetheryte, Exhausted }

public sealed class RecoveryLadder(int perRungCap = 2)
{
    private int _attemptsOnRung;

    public RecoveryRung Rung { get; private set; } = RecoveryRung.RePath;

    public RecoveryRung NextAttempt()
    {
        if (Rung == RecoveryRung.Exhausted) return Rung;
        if (_attemptsOnRung >= perRungCap)
        {
            Rung += 1;
            _attemptsOnRung = 0;
            if (Rung == RecoveryRung.Exhausted) return Rung;
        }
        _attemptsOnRung++;
        return Rung;
    }

    public void Reset()
    {
        Rung = RecoveryRung.RePath;
        _attemptsOnRung = 0;
    }
}
