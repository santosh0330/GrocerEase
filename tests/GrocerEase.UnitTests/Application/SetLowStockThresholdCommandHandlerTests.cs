using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Handlers;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class SetLowStockThresholdCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public SetLowStockThresholdCommandHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    private SetLowStockThresholdCommandHandler Handler() => new(new StoreAccess(_members), _products, _stock);

    private Product AddProduct(string name = "Milk", Guid? storeId = null, bool archived = false)
    {
        var product = new Product(Guid.CreateVersion7(), storeId ?? _storeId, name, "", 1m, archived ? DateTimeOffset.UtcNow : null);
        _products.Products.Add(product);
        return product;
    }

    [Fact]
    public async Task TheLevel_IsSet_AndTheEntryIsReturnedWithTheQuantityAndWhetherItIsLow()
    {
        var milk = AddProduct();
        _stock.Quantities[(_storeId, milk.Id)] = 4;

        var result = await Handler().Handle(new SetLowStockThresholdCommand(_storeId, milk.Id, _userId, 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((milk.Id, "Milk", 4, 5, true), (result.Value.ProductId, result.Value.ProductName, result.Value.QuantityOnHand, result.Value.LowStockThreshold, result.Value.IsLow));
        Assert.Equal(5, _stock.Thresholds[(_storeId, milk.Id)]);
        Assert.Equal(4, _stock.Quantities[(_storeId, milk.Id)]); // only the level was written
    }

    [Fact]
    public async Task AProductThatHasNeverHadStock_GetsItsRowAtZero_AndIsLowAtOnce_IfTheLevelIsAboveZero()
    {
        var milk = AddProduct();

        var result = await Handler().Handle(new SetLowStockThresholdCommand(_storeId, milk.Id, _userId, 3), CancellationToken.None);

        Assert.Equal((0, 3, true), (result.Value.QuantityOnHand, result.Value.LowStockThreshold, result.Value.IsLow));
        Assert.Equal(0, _stock.Quantities[(_storeId, milk.Id)]);
    }

    [Fact]
    public async Task NoLevel_ClearsIt_SoTheProductIsNoLongerLow()
    {
        var milk = AddProduct();
        _stock.Quantities[(_storeId, milk.Id)] = 1;
        _stock.Thresholds[(_storeId, milk.Id)] = 5;

        var result = await Handler().Handle(new SetLowStockThresholdCommand(_storeId, milk.Id, _userId, null), CancellationToken.None);

        Assert.Equal((1, (int?)null, false), (result.Value.QuantityOnHand, result.Value.LowStockThreshold, result.Value.IsLow));
        Assert.DoesNotContain((_storeId, milk.Id), _stock.Thresholds.Keys);
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsStoreNotFound_AndNothingIsWritten()
    {
        var milk = AddProduct();

        var notAMember = await Handler().Handle(new SetLowStockThresholdCommand(_storeId, milk.Id, Guid.CreateVersion7(), 5), CancellationToken.None);
        var unknownStore = await Handler().Handle(new SetLowStockThresholdCommand(Guid.CreateVersion7(), milk.Id, _userId, 5), CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, notAMember.Error);
        Assert.Same(StoreErrors.NotFound, unknownStore.Error);
        Assert.Empty(_stock.Thresholds);
        Assert.Empty(_stock.Quantities);
    }

    [Fact]
    public async Task AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreProductNotFound_AndNothingIsWritten()
    {
        var archived = AddProduct("Old", archived: true);
        var foreign = AddProduct("Elsewhere", Guid.CreateVersion7());

        foreach (var productId in new[] { Guid.CreateVersion7(), archived.Id, foreign.Id })
        {
            var result = await Handler().Handle(new SetLowStockThresholdCommand(_storeId, productId, _userId, 5), CancellationToken.None);

            Assert.Same(ProductErrors.NotFound, result.Error);
        }

        Assert.Empty(_stock.Thresholds);
        Assert.Empty(_stock.Quantities);
    }
}
