using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public sealed class RotationConfig
{
    public IReadOnlyList<ushort> Zones { get; init; } = [];
    public int QuietSeconds { get; init; } = 120;      // G2: this long without an eligible fate moves on
    public int MoveTimeoutSeconds { get; init; } = 60; // G4: a zone change that has not landed by then is given up on
}

// Farms a list of zones in turn (G1–G5): the starting zone when it is listed, the next one when
// the current has gone quiet, skipping a zone that cannot be reached, and stopping when none can.
public sealed class ZoneRotationModule(RotationConfig cfg) : IFarmModule
{
    private int _index = -1;
    private long? _movingSinceEpoch;
    private int _failures;

    public ushort? Target => _index >= 0 ? cfg.Zones[_index] : null;

    public ModuleDirective Next(WorldSnapshot w, ModuleContext ctx)
    {
        var zones = cfg.Zones;
        if (zones.Count == 0) return new FarmHere();

        if (_index < 0)
        {
            var here = IndexOf(zones, w.TerritoryId);
            _index = here >= 0 ? here : 0; // G1
        }

        var target = zones[_index];
        if (w.TerritoryId != target)
        {
            _movingSinceEpoch ??= w.NowEpoch;
            if (w.NowEpoch - _movingSinceEpoch >= cfg.MoveTimeoutSeconds)
            {
                // G4/G5: the zone change never landed (an unattuned aetheryte, a refused teleport)
                _failures++;
                if (_failures >= zones.Count)
                    return new StopSession(StopReason.ZonesUnreachable, "none of the listed zones could be reached");
                Advance(w.NowEpoch);
                return new MoveToTerritory(zones[_index]);
            }
            return new MoveToTerritory(target);
        }

        _movingSinceEpoch = null;
        _failures = 0;
        if (zones.Count > 1 && ctx.IdleSeconds >= cfg.QuietSeconds)
        {
            Advance(w.NowEpoch); // G2: quiet here, so the next zone
            return new MoveToTerritory(zones[_index]);
        }
        return new FarmHere();
    }

    private void Advance(long nowEpoch)
    {
        _index = (_index + 1) % cfg.Zones.Count;
        _movingSinceEpoch = nowEpoch;
    }

    private static int IndexOf(IReadOnlyList<ushort> zones, ushort territory)
    {
        for (var i = 0; i < zones.Count; i++)
            if (zones[i] == territory) return i;
        return -1;
    }
}
