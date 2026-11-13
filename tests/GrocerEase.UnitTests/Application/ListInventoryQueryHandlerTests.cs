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

    private ListInventoryQueryHandler Handler() => new(new StoreAccess(_members), _products, _stock);

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
            [new InventoryEntryResult(bread.Id, "Bread", 0), new InventoryEntryResult(milk.Id, "Milk", 7)], // ordered by name
            result.Value.Items);
        Assert.Equal(2, result.Value.TotalCount);
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
}
