using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Moirai.Execution;

public static unsafe class ZoneTravel
{
    // Teleport to the primary aetheryte of a territory (lowest Order wins, matching the map list).
    public static bool TeleportToTerritory(ushort territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        if (sheet is null) return false;

        uint best = 0;
        var bestOrder = uint.MaxValue;
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte) continue;
            if (row.Territory.RowId != territoryId) continue;
            if (row.Order < bestOrder)
            {
                bestOrder = row.Order;
                best = row.RowId;
            }
        }
        if (best == 0) return false;
        return TeleportToAetheryte(best);
    }

    public static bool TeleportToAetheryte(uint aetheryteId)
    {
        var telepo = Telepo.Instance();
        return telepo != null && telepo->Teleport(aetheryteId, 0);
    }
}
