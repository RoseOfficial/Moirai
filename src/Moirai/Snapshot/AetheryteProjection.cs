using System.Numerics;
using Moirai.Core.Model;

namespace Moirai.Snapshot;

// The current zone's attuned aetherytes with world positions, by row id (§4 A12, C17). The sheet is
// scanned on a territory change and rechecked every few seconds, since attuning to one mid-run is
// rare but possible.
public sealed unsafe class AetheryteProjection
{
    private const long RecheckMs = 10_000;

    private ushort _territory;
    private long _checkedMs = long.MinValue / 2;
    private IReadOnlyList<Aetheryte> _current = [];

    public IReadOnlyList<Aetheryte> Get(ushort territory)
    {
        var now = Environment.TickCount64;
        if (territory != _territory || now - _checkedMs >= RecheckMs)
        {
            _territory = territory;
            _checkedMs = now;
            _current = Scan(territory);
        }
        return _current;
    }

    private static IReadOnlyList<Aetheryte> Scan(ushort territory)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        if (sheet is null || ui == null) return [];

        var found = new List<Aetheryte>();
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territory) continue;
            if (!ui->IsAetheryteUnlocked(row.RowId)) continue; // attuned ones only: the rest cannot be teleported to
            if (Position(row) is { } position)
                found.Add(new Aetheryte(row.RowId, position));
        }
        return found;
    }

    // The aetheryte's placement from its first Level link; null when the row carries none
    private static Vector3? Position(Lumina.Excel.Sheets.Aetheryte row)
    {
        foreach (var link in row.Level)
        {
            if (link.RowId == 0) continue;
            if (link.ValueNullable is { } level)
                return new Vector3(level.X, level.Y, level.Z);
        }
        return null;
    }
}
