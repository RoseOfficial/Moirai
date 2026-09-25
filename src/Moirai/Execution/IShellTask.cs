namespace Moirai.Execution;

// Work the shell does for the planner across many frames: a purchase, a repair, a rank read, a
// shopping trip. Each is all game windows or a short walk, so the plugin ticks whichever is active
// every frame, outside the planner's busy guard, and holds the planner meanwhile; the planner keeps
// asking and its intent is a no-op until the task ends. A pause, a stop or a duty resets it.
public interface IShellTask
{
    bool IsActive { get; }
    string Status { get; }
    void Tick();
    void Reset();
}
