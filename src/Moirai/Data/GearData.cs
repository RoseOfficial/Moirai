namespace Moirai.Data;

// §7.5: Dark Matter by item id, lowest grade first. A piece that takes one grade can be mended with
// that grade or any higher one. Verified on xivapi: Grade 1–5 are 5594–5598, Grade 6 10386,
// Grade 7 17837, Grade 8 33916.
public static class GearData
{
    public static readonly IReadOnlyList<uint> DarkMatterGrades = [5594, 5595, 5596, 5597, 5598, 10386, 17837, 33916];
}
