using Moirai.Game;
using Moirai.Ipc;

namespace Moirai.Execution;

// §7.5 R1: mends the equipped gear with Dark Matter through the game's own Repair window: open it,
// press Repair All, confirm, wait while the game mends, close it. Every step is a game window, so
// this runs per frame from the plugin, outside the planner's busy guard, like the minion purchaser.
// A step that does not finish in its time fails with a chat line, and the failure switches repair
// off for the run (R5).
public sealed class GearRepairer(NavmeshIpc navmesh)
{
    private enum Step { Idle, Open, RepairAll, Confirm, Mending, Close, Done, Failed }

    public const string RepairAddon = "Repair";
    private const string ConfirmAddon = "SelectYesno";
    private const int RepairAllCallback = 0; // the window's Repair All, over the equipped list it opens on
    private const int CloseCallback = -1;
    private const long StepTimeoutMs = 10_000;
    private const long MendingTimeoutMs = 30_000;
    private const long PressGapMs = 500;     // a window needs a moment to answer a press
    private const long MendingSettleMs = 1_000;

    private Step _step = Step.Idle;
    private long _stepStartedMs;
    private long _lastPressMs;

    public bool IsActive => _step is not (Step.Idle or Step.Failed or Step.Done);
    public bool HasFailed => _step == Step.Failed;
    public string Status { get; private set; } = "";
    public string DebugState => $"step={_step}";

    // Idempotent while a repair runs; a failed repairer stays failed until the next run
    public void Begin()
    {
        if (IsActive || HasFailed) return;
        navmesh.Stop();
        Enter(Step.Open, "repairing gear: opening the Repair window");
    }

    public void Reset()
    {
        _step = Step.Idle;
        Status = "";
    }

    public void Tick()
    {
        if (!IsActive) return;
        var now = Environment.TickCount64;
        if (now - _stepStartedMs > (_step == Step.Mending ? MendingTimeoutMs : StepTimeoutMs))
        {
            Fail($"{_step} did not finish");
            return;
        }
        if (now - _lastPressMs < PressGapMs) return;

        switch (_step)
        {
            case Step.Open:
                if (GameEx.AddonReady(RepairAddon))
                    Enter(Step.RepairAll, "repairing gear: repair all");
                else
                    Press(GameEx.OpenRepair);
                break;

            case Step.RepairAll:
                if (GameEx.AddonReady(ConfirmAddon))
                    Enter(Step.Confirm, "repairing gear: confirming");
                else
                    Press(() => GameEx.FireAddonCallback(RepairAddon, true, RepairAllCallback));
                break;

            case Step.Confirm:
                if (!GameEx.AddonReady(ConfirmAddon))
                    Enter(Step.Mending, "repairing gear: mending");
                else
                    Press(() => GameEx.ClickYes());
                break;

            case Step.Mending:
                // the game holds its repair condition while it mends; give it a moment to take hold
                if (now - _stepStartedMs >= MendingSettleMs && !GameEx.Mending())
                    Enter(Step.Close, "repairing gear: closing the window");
                break;

            case Step.Close:
                if (!GameEx.AddonReady(RepairAddon))
                {
                    _step = Step.Done;
                    Status = "gear repaired";
                }
                else
                {
                    Press(() => GameEx.FireAddonCallback(RepairAddon, true, CloseCallback));
                }
                break;
        }
    }

    private void Press(Action press)
    {
        _lastPressMs = Environment.TickCount64;
        press();
    }

    private void Enter(Step step, string status)
    {
        _step = step;
        _stepStartedMs = Environment.TickCount64;
        _lastPressMs = 0;
        Status = status;
    }

    private void Fail(string why)
    {
        _step = Step.Failed;
        Status = $"gear repair failed: {why}";
        GameEx.FireAddonCallback(RepairAddon, true, CloseCallback); // leave no window holding the busy guard
        Svc.Chat.PrintError($"[Moirai] Gear repair stopped: {why}. Repair is off for this run; /moirai debug shows the gear and the repair step it stopped at.");
    }
}
