namespace Moirai.Core.Planning;

// D9/D10: fates ruled out for the rest of the session, each with the reason the gates report
public sealed class SessionSkipList
{
    private readonly Dictionary<uint, SkipReason> _reasons = [];

    public int Count => _reasons.Count;

    public IEnumerable<KeyValuePair<uint, SkipReason>> All => _reasons;

    public void Add(uint fateId, SkipReason reason) => _reasons[fateId] = reason;

    public SkipReason? Reason(uint fateId) => _reasons.TryGetValue(fateId, out var r) ? r : null;
}
