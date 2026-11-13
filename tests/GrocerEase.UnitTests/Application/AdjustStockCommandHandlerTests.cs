using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Commands;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Handlers;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class AdjustStockCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Product _milk;

    public AdjustStockCommandHandlerTests()
    {
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId)); // the user runs the store
        _milk = new Product(Guid.CreateVersion7(), _storeId, "Milk", "", 2.5m);
        _products.Products.Add(_milk);
    }

    private AdjustStockCommandHandler Handler() => new(new StoreAccess(_members), _products, _stock);

    private AdjustStockCommand Adjust(int delta) => new(_storeId, _milk.Id, _userId, delta);

    [Fact]
    public async Task AddingStock_ToAProductWithNoRow_MakesTheRow_AndReturnsTheQuantity()
    {
        var result = await Handler().Handle(Adjust(12), CancellationToken.None);

        Assert.Equal(new StockResult(_milk.Id, 12), result.Value);
        Assert.Equal(12, _stock.Quantities[(_storeId, _milk.Id)]);
    }

    [Fact]
    public async Task Changes_AreAmounts_SoTheyAddUp()
    {
        await Handler().Handle(Adjust(10), CancellationToken.None);
        await Handler().Handle(Adjust(5), CancellationToken.None);

        var result = await Handler().Handle(Adjust(-3), CancellationToken.None);

        Assert.Equal(12, result.Value.QuantityOnHand);
    }

    [Fact]
    public async Task RemovingMoreThanThereIs_IsNotEnoughStock_AndChangesNothing()
    {
        await Handler().Handle(Adjust(5), CancellationToken.None);

        var result = await Handler().Handle(Adjust(-6), CancellationToken.None);

        Assert.Same(InventoryErrors.NotEnoughStock, result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(5, _stock.Quantities[(_storeId, _milk.Id)]);
    }

    [Fact]
    public async Task RemovingFromAProductWithNoStockRow_IsNotEnoughStock()
    {
        var result = await Handler().Handle(Adjust(-1), CancellationToken.None);

        Assert.Same(InventoryErrors.NotEnoughStock, result.Error);
        Assert.Empty(_stock.Quantities);
    }

    [Fact]
    public async Task AddingPastTheMaximum_IsTooMuchStock_AndChangesNothing()
    {
        await Handler().Handle(Adjust(InventoryItem.MaxQuantity), CancellationToken.None);

        var result = await Handler().Handle(Adjust(1), CancellationToken.None);

        Assert.Same(InventoryErrors.TooMuchStock, result.Error);
        Assert.Equal(InventoryItem.MaxQuantity, _stock.Quantities[(_storeId, _milk.Id)]);
    }

    [Fact]
    public async Task RemovingExactlyWhatThereIs_IsFine_AndLeavesZero()
    {
        await Handler().Handle(Adjust(5), CancellationToken.None);

        var result = await Handler().Handle(Adjust(-5), CancellationToken.None);

        Assert.Equal(0, result.Value.QuantityOnHand);
    }

    [Fact]
    public async Task ANonMember_IsToldTheStoreIsNotFound_AndNothingChanges()
    {
        var result = await Handler().Handle(Adjust(5) with { ActorId = Guid.CreateVersion7() }, CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Empty(_stock.Quantities);
    }

    [Fact]
    public async Task TheOwnershipRule_IsCheckedBeforeTheProduct_SoAStrangerCannotLearnWhichProductsExist()
    {
        var stranger = Adjust(5) with { ActorId = Guid.CreateVersion7(), ProductId = Guid.CreateVersion7() }; // a product that does not exist

        var result = await Handler().Handle(stranger, CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error); // not Product.NotFound
    }

    [Fact]
    public async Task AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreNotFound()
    {
        var archived = new Product(Guid.CreateVersion7(), _storeId, "Old", "", 1m, DateTimeOffset.UtcNow);
        var foreign = new Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "Foreign", "", 1m);
        _products.Products.AddRange([archived, foreign]);

        foreach (var productId in new[] { Guid.CreateVersion7(), archived.Id, foreign.Id })
        {
            var result = await Handler().Handle(Adjust(5) with { ProductId = productId }, CancellationToken.None);

            Assert.Same(ProductErrors.NotFound, result.Error);
        }

        Assert.Empty(_stock.Quantities);
    }

    [Fact]
    public async Task EveryChangeThatIsMade_LeavesOneMovement_WithTheAmountTheUserAndNoOrder_AndTheMovementsAddUpToTheQuantity()
    {
        await Handler().Handle(Adjust(10), CancellationToken.None);
        await Handler().Handle(Adjust(-3), CancellationToken.None);
        var result = await Handler().Handle(Adjust(5), CancellationToken.None);

        Assert.Equal([(10, StockMovementReason.Received), (-3, StockMovementReason.Correction), (5, StockMovementReason.Received)], _stock.Movements.Select(m => (m.Delta, m.Reason)));
        Assert.All(_stock.Movements, movement => Assert.Equal((_userId, (Guid?)null, _milk.Id, _storeId), (movement.ActorId, movement.OrderId, movement.ProductId, movement.StoreId)));
        Assert.Equal(result.Value.QuantityOnHand, _stock.Movements.Sum(movement => movement.Delta)); // the ledger adds up
    }

    [Fact]
    public async Task ARefusedChange_LeavesNoMovement()
    {
        await Handler().Handle(Adjust(2), CancellationToken.None);

        var refused = await Handler().Handle(Adjust(-3), CancellationToken.None); // would go below zero

        Assert.Equal(InventoryErrors.NotEnoughStock, refused.Error);
        Assert.Equal([2], _stock.Movements.Select(movement => movement.Delta));
    }

    [Fact]
    public async Task AChangeForAProductThatIsNotFoundOrAStoreThatIsNotTheirs_LeavesNoMovement()
    {
        await Handler().Handle(new AdjustStockCommand(_storeId, Guid.CreateVersion7(), _userId, 5), CancellationToken.None);
        await Handler().Handle(new AdjustStockCommand(Guid.CreateVersion7(), _milk.Id, _userId, 5), CancellationToken.None);
        await Handler().Handle(new AdjustStockCommand(_storeId, _milk.Id, Guid.CreateVersion7(), 5), CancellationToken.None);

        Assert.Empty(_stock.Movements);
    }

    [Theory]
    [InlineData("Received", 5, StockMovementReason.Received)]
    [InlineData("received", 5, StockMovementReason.Received)]
    [InlineData("Correction", 5, StockMovementReason.Correction)] // a correction may add: the count was too low
    [InlineData("Correction", -5, StockMovementReason.Correction)]
    public async Task AReasonThePersonGave_IsTheOneRecorded_WhateverTheSign(string said, int delta, StockMovementReason expected)
    {
        await Handler().Handle(Adjust(10), CancellationToken.None);
        _stock.Movements.Clear();

        await Handler().Handle(Adjust(delta) with { Reason = said }, CancellationToken.None);

        Assert.Equal(expected, Assert.Single(_stock.Movements).Reason);
    }

    [Fact]
    public async Task WithNoReason_AnAdditionIsADelivery_AndARemovalIsACorrection()
    {
        await Handler().Handle(Adjust(10), CancellationToken.None);
        await Handler().Handle(Adjust(-4), CancellationToken.None);

        Assert.Equal([StockMovementReason.Received, StockMovementReason.Correction], _stock.Movements.Select(movement => movement.Reason));
    }
}
