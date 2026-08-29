using System.Numerics;

namespace Moirai.Core.Planning;

public sealed class StuckDetector(float minMove = 2f, long windowMs = 2000)
{
    private Vector3 _anchor;
    private long _anchorMs;
    private bool _primed;

    public bool Sample(Vector3 pos, long nowMs, bool suppress)
    {
        if (suppress || !_primed)
        {
            Anchor(pos, nowMs);
            return false;
        }
        if (Vector3.Distance(pos, _anchor) >= minMove)
        {
            Anchor(pos, nowMs);
            return false;
        }
        if (nowMs - _anchorMs >= windowMs)
        {
            _anchorMs = nowMs; // one report per elapsed window
            return true;
        }
        return false;
    }

    public void Reset() => _primed = false;

    private void Anchor(Vector3 pos, long nowMs)
    {
        _anchor = pos;
        _anchorMs = nowMs;
        _primed = true;
    }
}
