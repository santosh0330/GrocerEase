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
}
