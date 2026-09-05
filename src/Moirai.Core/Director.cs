using System.Numerics;
using Moirai.Core.Behaviors;
using Moirai.Core.Intents;
using Moirai.Core.Model;
using Moirai.Core.Modules;
using Moirai.Core.Planning;
using Moirai.Core.Session;

namespace Moirai.Core;

public enum RunPhase { Idle, SelectingFate, Traveling, InFate, WaitingContinuation, Stopped }

public sealed record DirectorOutput(Intent Intent, string Status);

public sealed class Director(
    IFarmModule module,
    SelectionConfig selection,
    DirectorConfig cfg,
    TravelBehavior travel,
    Func<FateKind, IBehavior> behaviorFactory,
    Func<WorldSnapshot, BehaviorContext> contextFactory,
    CompanionUpkeep? companion = null,
    StrayAggroClear? aggro = null)
{
    private readonly ContinuationWatcher _continuation = new();
    private readonly RecoveryLadder _ladder = new();
    private readonly StrayAggroClear _aggro = aggro ?? new(new EngageConfig());
    private readonly HashSet<uint> _deadly = []; // D9: fates we died in this session
    private IBehavior? _active;
    private FateKind _activeKind;
    private long? _lastFateEnd;
    private bool _deathCounted;

    public CompanionUpkeep? Companion => companion;
    public SessionLedger Ledger { get; } = new();
    public RewardLatch RewardLatch { get; } = new();
    public RunPhase Phase { get; private set; } = RunPhase.Idle;
    public StopReason? StoppedBecause { get; private set; }
    public FateSnapshot? CurrentFate { get; private set; }

    public void Start()
    {
        if (Phase is RunPhase.Idle or RunPhase.Stopped)
        {
            Phase = RunPhase.SelectingFate;
            StoppedBecause = null;
        }
    }

    public void Stop(StopReason reason)
    {
        Phase = RunPhase.Stopped;
        StoppedBecause = reason;
    }

    public DirectorOutput Tick(WorldSnapshot w)
    {
        if (Phase is RunPhase.Idle or RunPhase.Stopped)
            return new(new NoAction(), Phase == RunPhase.Stopped ? $"stopped: {StoppedBecause}" : "idle");

        RewardLatch.Observe(w);

        var interrupt = InterruptEvaluator.Evaluate(w, CurrentFate?.Id, inFatePhase: Phase == RunPhase.InFate);
        switch (interrupt)
        {
            case InterruptKind.Busy:
                return new(new Hold(250), "busy");
            case InterruptKind.Dead:
                return HandleDeath(w);
        }
        _deathCounted = false;

        // D3: stray aggro gets its turn on unexpected combat, and inside the fate whenever none of
        // the fate's own enemies is on us (those come first, in the fate's own mode). The clear
        // decides what can be fought: nothing from the saddle, never the fate's own mobs. Once
        // nothing is on us it stands the rotation down, and the fate's behavior starts over so
        // its own mode comes back.
        var strayTurn = interrupt == InterruptKind.UnexpectedCombat
                        || (Phase == RunPhase.InFate && !FateEnemyOnUs(w));
        if (strayTurn || _aggro.Fighting)
        {
            var clear = strayTurn ? _aggro.Tick(w, CurrentFate?.Id) : _aggro.StandDown();
            if (clear is not null)
            {
                if (clear.Status == BehaviorStatus.Done)
                {
                    _active?.Reset();
                    travel.Resume();
                }
                return new(clear.Intent, clear.Note);
            }
        }

        if (interrupt == InterruptKind.NavmeshNotReady)
            return new(new Hold(1000), "waiting for navmesh");

        // F9/F10: companion upkeep in settled moments only, never mid-leg
        if (companion is not null)
        {
            if (companion.StopWhenOutOfGreens && companion.OutOfGreens(w))
            {
                Stop(StopReason.OutOfGreens);
                return new(new StopRun(StopReason.OutOfGreens), "out of Gysahl Greens");
            }
            if (Phase != RunPhase.Traveling && companion.Tick(w) is { } upkeep)
                return new(upkeep, upkeep is SetCompanionStance ? "setting companion stance" : "summoning companion");
        }

        switch (module.Next(w))
        {
            case MoveToTerritory t:
                if (RewardLatch.IsPending)
                    return new(new Hold(1000), "waiting for fate rewards"); // D6
                CurrentFate = null;
                Phase = RunPhase.SelectingFate;
                return new(new ChangeZone(t.TerritoryId), "changing zone");
            case StopSession s:
                Stop(s.Reason);
                return new(new StopRun(s.Reason), s.Summary);
        }

        return Phase switch
        {
            RunPhase.SelectingFate => SelectFate(w),
            RunPhase.Traveling => Travel(w),
            RunPhase.InFate => InFate(w),
            RunPhase.WaitingContinuation => AwaitContinuation(w),
            _ => new(new NoAction(), "idle"),
        };
    }

    private DirectorOutput HandleDeath(WorldSnapshot w)
    {
        if (!_deathCounted)
        {
            _deathCounted = true;
            Ledger.RecordDeath();
            if (CurrentFate is { } f)
            {
                // D9: a fate we died in, or died inside of on the way in, is not tried again this
                // session: a solo death leaves a boss at full health, so the ranking would send us
                // straight back. A death on the road is the road's doing, not the fate's.
                if (Phase == RunPhase.InFate || f.Contains(w.Player.Position))
                    _deadly.Add(f.Id);
                Ledger.Record(FateOutcome.Abandoned); // D2: death is never a completion
                CurrentFate = null;
            }
            Phase = RunPhase.SelectingFate;
            if (Ledger.Deaths >= cfg.DeathCap)
            {
                Stop(StopReason.DeathCapReached);
                return new(new StopRun(StopReason.DeathCapReached), "death cap reached");
            }
        }
        return new(new AcceptReturn(), "dead: accepting return");
    }

    private DirectorOutput SelectFate(WorldSnapshot w)
    {
        var pick = FateRanker.PickBest(w, selection, _lastFateEnd, _deadly);
        if (pick is null)
            return new(new Hold(cfg.IdleHoldMs), "no eligible fates");
        CurrentFate = pick;
        travel.Reset();
        _ladder.Reset();
        Phase = RunPhase.Traveling;
        return new(new NoAction(), $"selected fate {pick.Id}");
    }

    private DirectorOutput Travel(WorldSnapshot w)
    {
        var live = CurrentFate is null ? null : w.FateById(CurrentFate.Id);
        if (live is null || live.Phase is FatePhase.Ended or FatePhase.Failed)
        {
            CurrentFate = null;
            Phase = RunPhase.SelectingFate;
            return new(new NoAction(), "fate gone before arrival");
        }
        CurrentFate = live;

        var step = travel.Tick(w, contextFactory(w) with { Fate = live });
        if (step.Status == BehaviorStatus.Done)
        {
            Dispatch(live.Kind);
            Phase = RunPhase.InFate;
            return new(new NoAction(), "arrived at fate");
        }
        if (step.Status == BehaviorStatus.Failed)
            return Recover(w);
        return new(step.Intent, step.Note);
    }

    private DirectorOutput InFate(WorldSnapshot w)
    {
        var live = CurrentFate is null ? null : w.FateById(CurrentFate.Id);
        if (live is null || live.Phase is FatePhase.Ended or FatePhase.Failed)
        {
            // a fate that vanished from the table after we fought it paid out as Ended
            var outcome = SessionLedger.OutcomeFrom(live?.Phase ?? FatePhase.Ended);
            Ledger.Record(outcome);
            _lastFateEnd = w.NowEpoch;
            var finished = CurrentFate!;
            CurrentFate = null;
            if (outcome == FateOutcome.Completed)
            {
                RewardLatch.Arm(finished.Id); // D6
                if (finished.HasContinuation)
                {
                    _continuation.Arm(finished, w.NowEpoch); // B8
                    Phase = RunPhase.WaitingContinuation;
                    return new(new SetCombat(false, CombatMode.Auto), "waiting for continuation");
                }
            }
            Phase = RunPhase.SelectingFate;
            return new(new SetCombat(false, CombatMode.Auto), $"fate {outcome}");
        }
        CurrentFate = live;

        // B11: the snapshot re-classified the fate under us (e.g. collect -> npc-start): run what it is now
        if (live.Kind != _activeKind)
        {
            Dispatch(live.Kind);
            return new(new NoAction(), $"fate is now {live.Kind}; re-dispatching");
        }

        var step = _active!.Tick(w, contextFactory(w) with { Fate = live });
        if (step.Status == BehaviorStatus.Failed)
            return Recover(w);
        if (step.Status == BehaviorStatus.Done)
        {
            // e.g. an NpcStart behavior finished opening the fate — its kind is now the real one
            Dispatch(live.Kind);
            return new(step.Intent, "behavior complete; re-dispatching");
        }
        return new(step.Intent, step.Note);
    }

    private void Dispatch(FateKind kind)
    {
        _active = behaviorFactory(kind);
        _activeKind = kind;
        _active.Reset();
    }

    private bool FateEnemyOnUs(WorldSnapshot w)
        => CurrentFate is { } f
           && w.Enemies.Any(e => e.IsAlive && e.FateId == f.Id && e.IsAttackingPlayer);

    private DirectorOutput AwaitContinuation(WorldSnapshot w)
    {
        var (state, adopted) = _continuation.Tick(w);
        switch (state)
        {
            case ContinuationState.Adopted:
                CurrentFate = adopted;
                travel.Reset();
                Phase = RunPhase.Traveling;
                return new(new NoAction(), $"continuation fate {adopted!.Id}");
            case ContinuationState.GaveUp:
                Phase = RunPhase.SelectingFate;
                return new(new NoAction(), "no continuation appeared");
            default:
                return new(new Hold(1000), "waiting for continuation");
        }
    }

    // §7.2: bounded, escalating, terminal (D7)
    private DirectorOutput Recover(WorldSnapshot w)
    {
        switch (_ladder.NextAttempt())
        {
            case RecoveryRung.RePath:
                return new(new NoAction(), "recovery: re-path");
            case RecoveryRung.RerollDestination:
                travel.RerollDropoff();
                return new(new NoAction(), "recovery: new dropoff");
            case RecoveryRung.VerticalEscape:
                var up = w.Player.Position with { Y = w.Player.Position.Y + 10 };
                return new(new GoTo(up, true, 2f), "recovery: vertical escape");
            case RecoveryRung.ReturnToAetheryte:
                var nearest = w.Aetherytes.MinBy(a => Vector3.Distance(a.Position, w.Player.Position));
                if (nearest is null) goto default;
                CurrentFate = null;
                Phase = RunPhase.SelectingFate;
                return new(new TeleportTo(nearest.Id), "recovery: returning to aetheryte");
            default:
                Stop(StopReason.StuckExhausted);
                return new(new StopRun(StopReason.StuckExhausted), "recovery exhausted");
        }
    }
}
