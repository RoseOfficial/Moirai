using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public sealed class CompanionConfig
{
    public bool Enabled { get; init; }
    public uint GreensItemId { get; init; }
    public uint StanceActionId { get; init; }          // BuddyAction row id; 0 leaves the stance alone
    public int ResummonBelowSeconds { get; init; } = 300;
    public bool StopWhenOutOfGreens { get; init; }
}

// Keeps the chocobo companion out and in the configured stance (F1–F8). Acts only on the
// snapshot; every retry is bounded and a zone change earns a fresh budget.
public sealed class CompanionUpkeep(CompanionConfig cfg)
{
    public const int SummonCooldownSeconds = 6;
    public const int MaxSummonAttempts = 3;
    public const int MaxStanceAttempts = 3;

    private long _lastSummonEpoch = long.MinValue / 2;
    private int _summonAttempts;
    private int _stanceAttempts;
    private ushort? _territory;

    public bool StopWhenOutOfGreens => cfg.Enabled && cfg.StopWhenOutOfGreens;

    // Why the companion is not being kept up, for the overlay; null while all is well
    public string? Note { get; private set; }

    public bool OutOfGreens(WorldSnapshot w)
        => cfg.Enabled && !w.Player.CompanionSummoned && w.CountOf(cfg.GreensItemId) == 0;

    public Intent? Tick(WorldSnapshot w)
    {
        if (!cfg.Enabled) return null;
        var p = w.Player;

        if (_territory != w.TerritoryId)
        {
            _territory = w.TerritoryId;
            _summonAttempts = 0;
            _stanceAttempts = 0;
            Note = null;
        }

        // F4: a green used while mounted is wasted, and a fight is never interrupted
        if (p.IsMounted || p.InCombat || p.IsCasting) return null;

        if (p.CompanionSummoned)
        {
            _summonAttempts = 0; // F8: it showed up
            if (cfg.StanceActionId != 0 && p.CompanionStanceId != cfg.StanceActionId)
            {
                if (_stanceAttempts >= MaxStanceAttempts)
                {
                    Note = "companion stance could not be set";
                    return null;
                }
                _stanceAttempts++;
                return new SetCompanionStance(cfg.StanceActionId); // F6
            }
            _stanceAttempts = 0;
        }

        // F1/F3: missing, or running low enough that another green extends it
        var needsGreens = !p.CompanionSummoned || p.CompanionTimeLeftSeconds < cfg.ResummonBelowSeconds;
        if (!needsGreens)
        {
            Note = null;
            return null;
        }

        if (w.CountOf(cfg.GreensItemId) == 0)
        {
            Note = "out of Gysahl Greens"; // F5
            return null;
        }
        if (w.NowEpoch - _lastSummonEpoch < SummonCooldownSeconds) return null; // F7
        if (_summonAttempts >= MaxSummonAttempts)
        {
            Note = "companion could not be summoned here"; // F8
            return null;
        }

        _summonAttempts++;
        _lastSummonEpoch = w.NowEpoch;
        Note = null;
        return new SummonCompanion(cfg.GreensItemId);
    }
}
