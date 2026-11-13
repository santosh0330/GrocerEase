using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class InventoryItemTests
{
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();

    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var item = new InventoryItem(ProductId, StoreId, 12);

        Assert.Equal((ProductId, StoreId, 12), (item.ProductId, item.StoreId, item.QuantityOnHand));
    }

    [Fact]
    public void Constructor_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(Guid.Empty, StoreId, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(ProductId, Guid.Empty, 1));
    }

    [Fact]
    public void TheQuantity_MayBeZeroOrTheMaximum_ButNotBelowZeroOrAboveTheMaximum()
    {
        Assert.Equal(0, new InventoryItem(ProductId, StoreId, 0).QuantityOnHand);
        Assert.Equal(InventoryItem.MaxQuantity, new InventoryItem(ProductId, StoreId, InventoryItem.MaxQuantity).QuantityOnHand);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(ProductId, StoreId, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(ProductId, StoreId, InventoryItem.MaxQuantity + 1));
    }

    [Fact]
    public void TheMaximum_IsSmallEnoughThatAQuantityPlusAChangeNeverOverflowsAnInt() =>
        // The statements add a change (at most the maximum, in either direction) to a quantity (at most the maximum).
        Assert.True((long)InventoryItem.MaxQuantity * 2 <= int.MaxValue);

    [Fact]
    public void ALevel_IsOptional_AndAProductWithNoneIsNeverLow()
    {
        var item = new InventoryItem(ProductId, StoreId, 0);

        Assert.Null(item.LowStockThreshold);
        Assert.False(item.IsLow); // even with nothing on hand: nobody asked to be told
    }

    [Fact]
    public void TheLevel_MayBeZeroOrTheMaximum_ButNotBelowZeroOrAboveTheMaximum()
    {
        Assert.Equal(0, new InventoryItem(ProductId, StoreId, 5, 0).LowStockThreshold);
        Assert.Equal(InventoryItem.MaxLowStockThreshold, new InventoryItem(ProductId, StoreId, 5, InventoryItem.MaxLowStockThreshold).LowStockThreshold);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(ProductId, StoreId, 5, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItem(ProductId, StoreId, 5, InventoryItem.MaxLowStockThreshold + 1));
    }

    // The boundary, both sides of it: at the level counts as low.
    [Theory]
    [InlineData(4, 5, true)]
    [InlineData(5, 5, true)]
    [InlineData(6, 5, false)]
    [InlineData(0, 0, true)] // a level of zero means "tell me when it is gone"
    [InlineData(1, 0, false)]
    [InlineData(0, null, false)]
    [InlineData(1_000, null, false)]
    public void IsLow_IsAtOrBelowTheLevel(int quantity, int? level, bool expected)
    {
        var item = new InventoryItem(ProductId, StoreId, quantity, level);

        Assert.Equal(expected, item.IsLow);
        Assert.Equal(expected, InventoryItem.IsLowExpression.Compile()(item)); // the property is the expression, compiled: there is no second definition
    }

    [Fact]
    public void TheExpression_IsWhatAQueryIsGiven_SoItIsAFilterOverStockRowsAndNotAMethodCall()
    {
        var items = new[] { new InventoryItem(ProductId, StoreId, 4, 5), new InventoryItem(ProductId, StoreId, 6, 5), new InventoryItem(ProductId, StoreId, 0) };

        // Applied to an IQueryable the way the repository applies it (here in memory): the same rows the property picks out.
        Assert.Equal(items.Where(item => item.IsLow), items.AsQueryable().Where(InventoryItem.IsLowExpression));
    }
}
