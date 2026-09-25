namespace Moirai.Core.Model;

// Which row of the currency exchange window (ShopExchangeCurrency) buys an item (gemstone design
// §7). In 7.x the window's values hold the row count, then per row its item id, its cost, and the
// index the purchase callback takes, at the positions below. A patch can move them, so a row counts
// only when its cost agrees with the catalog, and a window that lists none of the trader's known
// stock is a layout that moved rather than a trader that sells nothing: never a wrong row.
public static class ExchangeRows
{
    public const int RowCountValue = 4, ItemIdValues = 1066, CostValues = 456, RowIndexValues = 1310;
    private const int MaxRows = 200;

    public enum Outcome { NotReady, Listed, NotListed, Mismatch }

    public static (Outcome Outcome, int Row) Find(IReadOnlyList<uint>? values, uint itemId, int cost, IReadOnlyCollection<uint> knownIds)
    {
        if (values is null || values.Count <= RowCountValue) return (Outcome.NotReady, -1);
        var rows = (int)values[RowCountValue];
        if (rows <= 0 || rows > MaxRows || values.Count < RowIndexValues + rows) return (Outcome.NotReady, -1);

        var anyKnown = false;
        for (var i = 0; i < rows; i++)
        {
            var id = values[ItemIdValues + i];
            if (knownIds.Contains(id)) anyKnown = true;
            if (id != itemId) continue;
            return values[CostValues + i] == cost
                ? (Outcome.Listed, (int)values[RowIndexValues + i])
                : (Outcome.Mismatch, -1);
        }
        return (anyKnown ? Outcome.NotListed : Outcome.Mismatch, -1);
    }
}
