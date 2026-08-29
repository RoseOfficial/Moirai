using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace Moirai.Ipc;

public sealed class NavmeshIpc
{
    private readonly ICallGateSubscriber<bool> _isReady;
    private readonly ICallGateSubscriber<float> _buildProgress;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;
    private readonly ICallGateSubscriber<bool> _pathfindInProgress;
    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> _moveCloseTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;

    public NavmeshIpc()
    {
        var pi = Svc.PluginInterface;
        _isReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _buildProgress = pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        _pathIsRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _pathStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _moveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        _pointOnFloor = pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    }

    public bool IsReady()
    {
        try
        {
            if (!_isReady.InvokeFunc()) return false;
            var progress = _buildProgress.InvokeFunc();
            return progress < 0f || progress >= 1f;
        }
        catch
        {
            return false; // vnavmesh missing or unloaded
        }
    }

    public bool PathIsRunning()
    {
        try { return _pathIsRunning.InvokeFunc() || _pathfindInProgress.InvokeFunc(); }
        catch { return false; }
    }

    public bool MoveCloseTo(Vector3 dest, bool fly, float range)
    {
        try { return _moveCloseTo.InvokeFunc(dest, fly, range); }
        catch { return false; }
    }

    public void Stop()
    {
        try { _pathStop.InvokeAction(); }
        catch { /* not installed */ }
    }

    public Vector3? PointOnFloor(Vector3 near)
    {
        try
        {
            // search downward from +50y, landable only, 5y horizontal half-extent
            return _pointOnFloor.InvokeFunc(near with { Y = near.Y + 50f }, false, 5f);
        }
        catch
        {
            return null;
        }
    }
}
