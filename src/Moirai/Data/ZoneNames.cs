namespace Moirai.Data;

// Display only: the planner keys zones by territory id
public static class ZoneNames
{
    private static readonly Dictionary<ushort, string> Cache = [];

    public static string Name(ushort territory)
    {
        if (Cache.TryGetValue(territory, out var known)) return known;
        string name;
        try
        {
            var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRowOrDefault(territory);
            var place = row?.PlaceName.ValueNullable?.Name.ExtractText();
            name = string.IsNullOrWhiteSpace(place) ? $"Zone {territory}" : place;
        }
        catch
        {
            name = $"Zone {territory}";
        }
        Cache[territory] = name;
        return name;
    }
}
