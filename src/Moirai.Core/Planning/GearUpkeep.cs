using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public sealed class GearConfig
{
    public bool Enabled { get; init; }
    public int RepairBelowPercent { get; init; } = 30;
    public bool StopWhenBroken { get; init; }
}

// §7.5: keeps the equipped gear mended with Dark Matter (R1–R7). Acts only on the snapshot: the
// shell says which pieces self-repair can mend, and every retry is bounded. The Director asks
// between fates only.
public sealed class GearUpkeep(GearConfig cfg)
{
    public const int MaxAttempts = 2;   // R4: repairs in a row that did not bring the gear back up
    public const int RetrySeconds = 30; // R4: the wait before asking again

    private int _attempts;
    private long _lastAskEpoch = long.MinValue / 2;
    private bool _gaveUp;

    // Why the gear is not being kept up, for the overlay; null while all is well
    public string? Note { get; private set; }
    public bool StopWhenBroken => cfg.StopWhenBroken;

    // R7: a piece at 0% that no repair is coming for
    public bool Broken(WorldSnapshot w)
        => w.Player.Gear?.Any(g => g.ConditionPercent == 0 && !RepairComing(g, w)) == true;

    public Intent? Tick(WorldSnapshot w)
    {
        if (!cfg.Enabled) return null; // R6

        var low = (w.Player.Gear ?? []).Where(g => g.ConditionPercent < cfg.RepairBelowPercent).ToList();
        if (low.Count == 0)
        {
            _attempts = 0; // R4: mended, by us or by other means, so the next wear-down gets a fresh budget
            _gaveUp = false;
            Note = null;
            return null;
        }
        var lowest = low.Min(g => g.ConditionPercent);
        if (!low.Any(g => g.SelfRepairable))
        {
            Note = $"gear at {lowest}% cannot be self-repaired: a crafter level or the right Dark Matter is missing"; // R2
            return null;
        }
        if (!w.RepairReady)
        {
            Note = "the Repair window could not be worked; gear repair is off for this run"; // R5
            return null;
        }
        if (_gaveUp) return null;

        var p = w.Player;
        if (p.IsMounted || p.InCombat || p.IsCasting) return null; // R3
        if (w.NowEpoch - _lastAskEpoch < RetrySeconds) return null;
        if (_attempts >= MaxAttempts)
        {
            _gaveUp = true;
            Note = $"gear at {lowest}% did not come back up after {MaxAttempts} repairs; not trying again this run";
            return null;
        }

        _attempts++;
        _lastAskEpoch = w.NowEpoch;
        Note = null;
        return new RepairGear();
    }

    private bool RepairComing(GearPiece piece, WorldSnapshot w)
        => cfg.Enabled && piece.SelfRepairable && w.RepairReady && !_gaveUp;
}
