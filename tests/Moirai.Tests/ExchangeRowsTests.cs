using Moirai.Core.Model;

namespace Moirai.Tests;

public class ExchangeRowsTests
{
    private static uint[] Window(params (uint Item, uint Cost, uint Row)[] rows)
    {
        var v = new uint[ExchangeRows.RowIndexValues + 60];
        v[ExchangeRows.RowCountValue] = (uint)rows.Length;
        for (var i = 0; i < rows.Length; i++)
        {
            v[ExchangeRows.ItemIdValues + i] = rows[i].Item;
            v[ExchangeRows.CostValues + i] = rows[i].Cost;
            v[ExchangeRows.RowIndexValues + i] = rows[i].Row;
        }
        return v;
    }

    [Fact] // gemstone design §7: the row is the window's own index for the item, never its position
    public void Finds_the_row_by_item_id()
    {
        var found = ExchangeRows.Find(Window((44053, 3, 0), (43607, 90, 7)), 43607, 90, [43607]);
        Assert.Equal((ExchangeRows.Outcome.Listed, 7), found);
    }

    [Fact] // §7: a window still filling gives no answer
    public void Not_ready_while_the_values_are_not_filled()
    {
        Assert.Equal(ExchangeRows.Outcome.NotReady, ExchangeRows.Find(null, 43607, 90, [43607]).Outcome);
        Assert.Equal(ExchangeRows.Outcome.NotReady, ExchangeRows.Find(new uint[10], 43607, 90, [43607]).Outcome);
        Assert.Equal(ExchangeRows.Outcome.NotReady, ExchangeRows.Find(Window(), 43607, 90, [43607]).Outcome);
    }

    [Fact] // S11: a filled window that lists the trader's other stock but not the item has not listed it
    public void Not_listed_when_the_item_is_missing()
    {
        var found = ExchangeRows.Find(Window((43961, 100, 3)), 43607, 90, [43607, 43961]);
        Assert.Equal(ExchangeRows.Outcome.NotListed, found.Outcome);
    }

    [Fact] // review focus: values a patch moved give a mismatch, never a wrong row
    public void Mismatch_when_the_cost_disagrees_or_nothing_known_is_listed()
    {
        Assert.Equal(ExchangeRows.Outcome.Mismatch, ExchangeRows.Find(Window((43607, 450, 7)), 43607, 90, [43607]).Outcome);
        Assert.Equal(ExchangeRows.Outcome.Mismatch, ExchangeRows.Find(Window((1, 2, 3), (4, 5, 6)), 43607, 90, [43607, 43961]).Outcome);
    }
}
