using Moirai.Core.Modules;

namespace Moirai.Tests;

public class GemstoneCatalogTests
{
    [Fact] // Appendix T: every item's trader is in the catalog
    public void Every_item_has_a_listed_trader()
        => Assert.All(GemstoneCatalog.Items, i => Assert.Contains(GemstoneCatalog.Traders, t => t.NpcId == i.TraderId));

    [Fact] // Appendix T: the collectibles cost 13,240 gemstones in all, 8,990 of them at the zone traders
    public void Collectibles_add_up()
    {
        var collectibles = GemstoneCatalog.Items.Where(i => i.Kind == GemstoneItemKind.Collectible).ToList();
        Assert.Equal(13_240, collectibles.Sum(i => i.Cost));
        Assert.Equal(8_990, collectibles.Where(i => !i.RequiresAllZonesMaxed).Sum(i => i.Cost));
        Assert.All(collectibles, i => Assert.Equal(GemstoneCatalog.Gemstone, i.CurrencyItemId));
    }

    [Fact] // S7: each zone trader lists its riding map first, at rank 2; the rest of its stock is rank 4
    public void Zone_traders_list_their_map_first()
    {
        foreach (var trader in GemstoneCatalog.Traders.Where(t => GemstoneCatalog.Zones.Contains(t.TerritoryId)))
        {
            var stock = GemstoneCatalog.Items.Where(i => i.TraderId == trader.NpcId && i.Kind == GemstoneItemKind.Collectible).ToList();
            Assert.Equal(2, stock[0].RequiredRank);
            Assert.All(stock.Skip(1), i => Assert.Equal(4, i.RequiredRank));
        }
    }

    [Fact] // S8, field report: every zone trader sells crafting materials from rank 1, 3 gemstones each (17 in all)
    public void Zone_traders_sell_materials_from_rank_1()
    {
        var materials = GemstoneCatalog.Items.Where(i => i.Kind == GemstoneItemKind.Material).ToList();
        Assert.Equal(17, materials.Count);
        Assert.All(materials, i => Assert.Equal((3, 1, GemstoneCatalog.Gemstone), (i.Cost, i.RequiredRank, i.CurrencyItemId)));
        foreach (var trader in GemstoneCatalog.Traders.Where(t => GemstoneCatalog.Zones.Contains(t.TerritoryId)))
            Assert.Contains(materials, i => i.TraderId == trader.NpcId);
    }

    [Fact] // S9: the whistle costs 500 Turali vouchers at Edelina
    public void The_mount_is_bought_with_turali_vouchers()
    {
        var whistle = GemstoneCatalog.Items.Single(i => i.Kind == GemstoneItemKind.Mount);
        Assert.Equal((GemstoneCatalog.TyaityaWhistle, GemstoneCatalog.Edelina, 500, GemstoneCatalog.TuraliVoucher),
            (whistle.ItemId, whistle.TraderId, whistle.Cost, whistle.CurrencyItemId));
    }

    [Fact] // S8: Turali vouchers wait for the city traders; the Endwalker one is always on offer
    public void Voucher_gates()
    {
        var vouchers = GemstoneCatalog.Items.Where(i => i.Kind == GemstoneItemKind.Voucher).ToList();
        Assert.All(vouchers.Where(i => i.ItemId == GemstoneCatalog.TuraliVoucher), i => Assert.True(i.RequiresAllZonesMaxed));
        Assert.False(vouchers.Single(i => i.ItemId == GemstoneCatalog.EndwalkerVoucher).RequiresAllZonesMaxed);
    }
}
