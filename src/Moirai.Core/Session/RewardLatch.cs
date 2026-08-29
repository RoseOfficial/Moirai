using Moirai.Core.Model;

namespace Moirai.Core.Session;

// D6: hold zone changes until the completed fate's payout registers,
// observed as the fate leaving the fate table.
public sealed class RewardLatch
{
    private uint _pendingFateId;

    public bool IsPending => _pendingFateId != 0;

    public void Arm(uint fateId) => _pendingFateId = fateId;

    public void Observe(WorldSnapshot w)
    {
        if (IsPending && w.FateById(_pendingFateId) is null)
            _pendingFateId = 0;
    }
}
