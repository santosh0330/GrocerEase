using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Handlers;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListInventoryQueryHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public ListInventoryQueryHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    private ListInventoryQueryHandler Handler() => new(new StoreAccess(_members), _products, _stock, new FakeLowStockRepository(_products, _stock));

    private Product AddProduct(string name, int? quantity = null, Guid? storeId = null, bool archived = false)
    {
        var product = new Product(Guid.CreateVersion7(), storeId ?? _storeId, name, "", 1m, archived ? DateTimeOffset.UtcNow : null);
        _products.Products.Add(product);
        if (quantity is { } q)
        {
            _stock.Quantities[(product.StoreId, product.Id)] = q;
        }

        return product;
    }

    [Fact]
    public async Task EveryLiveProduct_IsListedWithItsQuantity_AndZeroWhenItHasNoRow()
    {
        var milk = AddProduct("Milk", 7);
        var bread = AddProduct("Bread");

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 1, 20), CancellationToken.None);

        Assert.Equal(
            [new InventoryEntryResult(bread.Id, "Bread", 0), new InventoryEntryResult(milk.Id, "Milk", 7)], // ordered by name, no level set
            result.Value.Items);
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task EachEntryCarriesItsLowStockLevel_AndWhetherItIsLow_AtTheLevelCounting()
    {
        var below = AddProduct("Below", 4);
        var at = AddProduct("At", 5);
        var above = AddProduct("Above", 6);
        AddProduct("NoLevel", 0);
        AddProduct("NoRow");
        foreach (var product in new[] { below, at, above })
        {
            _stock.Thresholds[(_storeId, product.Id)] = 5;
        }

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 1, 20), CancellationToken.None);

        Assert.Equal(
            [("Above", 6, 5, false), ("At", 5, 5, true), ("Below", 4, 5, true), ("NoLevel", 0, null, false), ("NoRow", 0, null, false)],
            result.Value.Items.Select(entry => (entry.ProductName, entry.QuantityOnHand, entry.LowStockThreshold, entry.IsLow)));
    }

    [Fact]
    public async Task ArchivedProductsAndOtherStoresProducts_AreNotListed_AndTheirStockIsNotShown()
    {
        AddProduct("Milk", 7);
        AddProduct("Old", 3, archived: true);
        AddProduct("Elsewhere", 9, storeId: Guid.CreateVersion7());

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 1, 20), CancellationToken.None);

        Assert.Equal(["Milk"], result.Value.Items.Select(entry => entry.ProductName), StringComparer.Ordinal);
    }

    [Fact]
    public async Task APage_HoldsOnlyItsProducts_WithTheirOwnQuantities()
    {
        foreach (var (name, quantity) in new[] { ("A", 1), ("B", 2), ("C", 3), ("D", 4), ("E", 5) })
        {
            AddProduct(name, quantity);
        }

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 2, 2), CancellationToken.None);

        Assert.Equal([("C", 3), ("D", 4)], result.Value.Items.Select(entry => (entry.ProductName, entry.QuantityOnHand)));
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task ANonMember_IsToldTheStoreIsNotFound_AndSeesNoStock()
    {
        AddProduct("Milk", 7);

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, Guid.CreateVersion7(), 1, 20), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task LowStockOnly_ListsTheProductsAtOrBelowTheirLevel_AndNothingElse()
    {
        var below = AddProduct("Below", 4);
        var at = AddProduct("At", 5);
        var above = AddProduct("Above", 6);
        AddProduct("NoLevel", 0); // nothing on hand, but nobody asked to be told
        AddProduct("NoRow");
        var archived = AddProduct("Archived", 1, archived: true);
        var elsewhere = AddProduct("Elsewhere", 1, storeId: Guid.CreateVersion7());
        foreach (var product in new[] { below, at, above, archived })
        {
            _stock.Thresholds[(_storeId, product.Id)] = 5;
        }

        _stock.Thresholds[(elsewhere.StoreId, elsewhere.Id)] = 5;

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 1, 20, LowStockOnly: true), CancellationToken.None);

        Assert.Equal([(at.Id, 5), (below.Id, 4)], result.Value.Items.Select(entry => (entry.ProductId, entry.QuantityOnHand))); // by name; At is on the boundary
        Assert.All(result.Value.Items, entry => Assert.True(entry.IsLow));
        Assert.Equal(2, result.Value.TotalCount);
    }

    [Fact]
    public async Task LowStockOnly_IsPaged_WithTheTotalOfAllTheLowOnes()
    {
        foreach (var name in new[] { "D", "B", "A", "C", "E" })
        {
            var product = AddProduct(name, 1);
            _stock.Thresholds[(_storeId, product.Id)] = 5;
        }

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 2, 2, LowStockOnly: true), CancellationToken.None);

        Assert.Equal(["C", "D"], result.Value.Items.Select(entry => entry.ProductName), StringComparer.Ordinal);
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task LowStockOnly_ForAStoreTheCallerDoesNotRun_IsStoreNotFound()
    {
        var product = AddProduct("Milk", 1);
        _stock.Thresholds[(_storeId, product.Id)] = 5;

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, Guid.CreateVersion7(), 1, 20, LowStockOnly: true), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task WithoutTheFlag_EveryProductIsListed_LowOrNot()
    {
        var low = AddProduct("Low", 1);
        AddProduct("Fine", 100);
        _stock.Thresholds[(_storeId, low.Id)] = 5;

        var result = await Handler().Handle(new ListInventoryQuery(_storeId, _userId, 1, 20), CancellationToken.None);

        Assert.Equal(["Fine", "Low"], result.Value.Items.Select(entry => entry.ProductName), StringComparer.Ordinal);
    }
}
