using Moirai.Core.Model;

namespace Moirai.Core.Planning;

public enum SkipReason
{
    None, WrongPhase, NotRegistered, Blacklisted, AboveLevel,
    TooLittleTime, TooFarAlong, BossTooEarly, NotBonus, KilledUs, Unreachable, CollectFate, NpcStart,
}

public static class SelectionGates
{
    // skips: the fates ruled out for this session (D9 died in, D10 unreachable), kept by the Director
    public static SkipReason Evaluate(FateSnapshot f, WorldSnapshot w, SelectionConfig c, SessionSkipList? skips = null)
    {
        if (f.Phase is not (FatePhase.Running or FatePhase.Preparing)) return SkipReason.WrongPhase;
        if (f.Position.X == 0 && f.Position.Z == 0) return SkipReason.NotRegistered;
        if (c.Blacklist.Contains(f.Id)) return SkipReason.Blacklisted;
        if (skips?.Reason(f.Id) is { } skipped) return skipped;
        if (c.SkipCollectFates && f.SheetKind == FateKind.Collect) return SkipReason.CollectFate; // A14
        // A15: collect fates open at their hand-in NPC and follow the collect switch instead
        if (c.SkipNpcStartFates && f.Kind == FateKind.NpcStart && f.SheetKind != FateKind.Collect) return SkipReason.NpcStart;
        if (f.MaxLevel > w.Player.Level + c.LevelMargin) return SkipReason.AboveLevel;
        if (f.EffectiveTimeLeft < c.MinTimeLeftSeconds) return SkipReason.TooLittleTime;
        if (f.Progress > c.MaxProgressPercent) return SkipReason.TooFarAlong;
        if (f.Kind == FateKind.Boss)
        {
            var floor = f.IsSpecialBoss ? c.SpecialBossJoinProgress : c.BossJoinProgress;
            if (f.Progress < floor) return SkipReason.BossTooEarly;
        }
        if (c.BonusOnly && !f.IsBonus) return SkipReason.NotBonus;
        return SkipReason.None;
    }
}
