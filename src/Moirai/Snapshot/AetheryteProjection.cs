using System.Numerics;
using Moirai.Core.Model;
using Moirai.Ipc;

namespace Moirai.Snapshot;

// The current zone's attuned aetherytes with world positions, by row id (§4 A12, C17). The sheet is
// scanned on a territory change and rechecked every few seconds, since attuning to one mid-run is
// rare but possible.
public sealed unsafe class AetheryteProjection(NavmeshIpc navmesh)
{
    private const long RecheckMs = 10_000;
    private const int AetheryteMarker = 3; // MapMarker data type: the key is an Aetheryte row

    private ushort _territory;
    private long _checkedMs = long.MinValue / 2;
    private IReadOnlyList<Aetheryte> _current = [];

    public IReadOnlyList<Aetheryte> Get(ushort territory, float playerY)
    {
        var now = Environment.TickCount64;
        if (territory != _territory || now - _checkedMs >= RecheckMs)
        {
            _territory = territory;
            _checkedMs = now;
            _current = Scan(territory, playerY);
        }
        return _current;
    }

    private List<Aetheryte> Scan(ushort territory, float playerY)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (sheet is null || ui == null) return [];

        var found = new List<Aetheryte>();
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territory) continue;
            if (!ui->IsAetheryteUnlocked(row.RowId)) continue; // attuned ones only: the rest cannot be teleported to
            if (Position(row, playerY) is { } position)
                found.Add(new Aetheryte(row.RowId, position));
        }
        return found;
    }

    // The aetheryte's placement: its Level link when the sheet still has that row. The current game
    // data points every aetheryte's link at a row the Level sheet no longer carries, so the map
    // marker gives the spot, and the mesh around it the height.
    private Vector3? Position(Lumina.Excel.Sheets.Aetheryte row, float playerY)
    {
        foreach (var link in row.Level)
        {
            if (link.RowId == 0) continue;
            if (link.ValueNullable is { } level)
                return new Vector3(level.X, level.Y, level.Z);
        }
        if (MarkerPosition(row) is not { } flat) return null;
        var guess = flat with { Y = playerY };
        return navmesh.NearestPoint(guess, 5f, 500f) ?? guess;
    }

    // The aetheryte's marker on its own map, in world X/Z: pixel less 1024, over the map's scale,
    // less its offset
    private static Vector3? MarkerPosition(Lumina.Excel.Sheets.Aetheryte row)
    {
        if (row.Map.ValueNullable is not { } map || map.SizeFactor == 0) return null;
        var markers = Svc.Data.GetSubrowExcelSheet<Lumina.Excel.Sheets.MapMarker>();
        if (markers is null || !markers.TryGetRow(map.MapMarkerRange, out var range)) return null;
        var scale = map.SizeFactor / 100f;
        foreach (var m in range)
            if (m.DataType == AetheryteMarker && m.DataKey.RowId == row.RowId)
                return new Vector3((m.X - 1024f) / scale - map.OffsetX, 0f, (m.Y - 1024f) / scale - map.OffsetY);
        return null;
    }
}
