using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Core.Session;

public enum ContinuationState { Idle, Waiting, Adopted, GaveUp }

public sealed class ContinuationWatcher(long timeoutSeconds = 30, float sameSiteRadius = 60f)
{
    private bool _armed;
    private long _armedAt;
    private Vector3 _site;
    private uint _oldId;

    public void Arm(FateSnapshot completed, long nowEpoch)
    {
        _armed = true;
        _armedAt = nowEpoch;
        _site = completed.Position;
        _oldId = completed.Id;
    }

    public (ContinuationState, FateSnapshot?) Tick(WorldSnapshot w)
    {
        if (!_armed) return (ContinuationState.Idle, null);

        var adopt = w.Fates.FirstOrDefault(f =>
            f.Id != _oldId
            && f.Phase is FatePhase.Running or FatePhase.Preparing
            && Vector3.Distance(f.Position, _site) <= sameSiteRadius);
        if (adopt is not null)
        {
            _armed = false;
            return (ContinuationState.Adopted, adopt);
        }
        if (w.NowEpoch - _armedAt >= timeoutSeconds)
        {
            _armed = false;
            return (ContinuationState.GaveUp, null);
        }
        return (ContinuationState.Waiting, null);
    }

    public void Reset() => _armed = false;
}
