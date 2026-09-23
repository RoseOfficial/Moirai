using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Moirai.Execution;

public static unsafe class ZoneTravel
{
    // Teleport to the first attuned aetheryte of a territory in map-list order (lowest Order), so a
    // zone whose main aetheryte is not attuned is still reached through another. False when none
    // there is attuned; the module's move timeout then skips the zone (G4).
    public static bool TeleportToTerritory(ushort territoryId)
    {
        var sheet = Svc.Data.GetExcelSheet<Aetheryte>();
        var ui = UIState.Instance();
        if (sheet is null || ui == null) return false;

        uint best = 0;
        var bestOrder = uint.MaxValue;
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte) continue;
            if (row.Territory.RowId != territoryId) continue;
            if (!ui->IsAetheryteUnlocked(row.RowId)) continue;
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
