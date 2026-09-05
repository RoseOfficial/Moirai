namespace Moirai.Core.Replay;

// The newest status changes of a run with the time of each, for the debug report: one line per
// change, so a reader sees where a run went wrong without opening a recording
public sealed class StatusTimeline(int capacity = 50)
{
    private readonly Queue<(long Epoch, string Status)> _entries = new();
    private string? _last;

    public IReadOnlyList<(long Epoch, string Status)> Entries => [.. _entries];

    public void Observe(long nowEpoch, string status)
    {
        if (status == _last) return;
        _last = status;
        _entries.Enqueue((nowEpoch, status));
        while (_entries.Count > capacity)
            _entries.Dequeue();
    }
}
