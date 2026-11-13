using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Application.Inventory.Handlers;
using GrocerEase.Application.Inventory.Queries;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListStockMovementsQueryHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();
    private readonly Product _milk;

    public ListStockMovementsQueryHandlerTests()
    {
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));
        _milk = new Product(Guid.CreateVersion7(), _storeId, "Milk", "", 2.5m);
        _products.Products.Add(_milk);
    }

    private ListStockMovementsQueryHandler Handler() => new(new StoreAccess(_members), _products, _stock);

    private Task<Result<PagedResult<StockMovementResult>>> ListAsync(
        Guid? productId = null, Guid? storeId = null, Guid? actorId = null, int page = 1, int pageSize = 20) =>
        Handler().Handle(new ListStockMovementsQuery(storeId ?? _storeId, productId ?? _milk.Id, actorId ?? _userId, page, pageSize), CancellationToken.None);

    private void Move(int delta, StockMovementReason reason, Guid? order = null, int minutesAgo = 0) =>
        _stock.Movements.Add(new StockMovement(Guid.CreateVersion7(), _milk.Id, _storeId, delta, reason, _userId, order, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)));

    [Fact]
    public async Task TheHistory_IsNewestFirst_WithEveryFieldOfEachMovement()
    {
        var order = Guid.CreateVersion7();
        Move(10, StockMovementReason.Received, minutesAgo: 30);
        Move(-3, StockMovementReason.OrderPlaced, order, minutesAgo: 20);
        Move(3, StockMovementReason.OrderCancelled, order, minutesAgo: 10);

        var result = await ListAsync();

        Assert.Equal(
            [("OrderCancelled", 3, (Guid?)order), ("OrderPlaced", -3, order), ("Received", 10, null)],
            result.Value.Items.Select(movement => (movement.Reason, movement.Delta, movement.OrderId)));
        Assert.All(result.Value.Items, movement => Assert.Equal((_milk.Id, (Guid?)_userId), (movement.ProductId, movement.ActorId)));
        Assert.Equal(3, result.Value.TotalCount);
    }

    [Fact]
    public async Task APage_HoldsOnlyItsMovements_WithTheTotalOfAllOfThem()
    {
        for (var i = 0; i < 5; i++)
        {
            Move(i + 1, StockMovementReason.Received, minutesAgo: 50 - i); // 50, 49, ... 46 minutes ago: the newest is delta 5
        }

        var result = await ListAsync(page: 2, pageSize: 2);

        Assert.Equal([3, 2], result.Value.Items.Select(movement => movement.Delta));
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task AProductWithNoHistory_IsAnEmptyPage_NotAnError()
    {
        var result = await ListAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsStoreNotFound_LikeAStoreThatDoesNotExist()
    {
        Move(5, StockMovementReason.Received);

        var notAMember = await ListAsync(actorId: Guid.CreateVersion7());
        var unknownStore = await ListAsync(storeId: Guid.CreateVersion7());

        Assert.Same(StoreErrors.NotFound, notAMember.Error);
        Assert.Same(StoreErrors.NotFound, unknownStore.Error);
    }

    [Fact]
    public async Task AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreProductNotFound()
    {
        var archived = new Product(Guid.CreateVersion7(), _storeId, "Old", "", 1m, DateTimeOffset.UtcNow);
        var foreign = new Product(Guid.CreateVersion7(), Guid.CreateVersion7(), "Elsewhere", "", 1m);
        _products.Products.AddRange([archived, foreign]);

        foreach (var productId in new[] { Guid.CreateVersion7(), archived.Id, foreign.Id })
        {
            Assert.Same(ProductErrors.NotFound, (await ListAsync(productId: productId)).Error);
        }
    }

    [Fact]
    public async Task OtherProductsAndOtherStoresMovements_AreNeverInTheHistory()
    {
        Move(5, StockMovementReason.Received);
        var bread = new Product(Guid.CreateVersion7(), _storeId, "Bread", "", 1m);
        _products.Products.Add(bread);
        _stock.Movements.Add(new StockMovement(Guid.CreateVersion7(), bread.Id, _storeId, 9, StockMovementReason.Received, _userId, null, DateTimeOffset.UtcNow));
        _stock.Movements.Add(new StockMovement(Guid.CreateVersion7(), _milk.Id, Guid.CreateVersion7(), 99, StockMovementReason.Received, _userId, null, DateTimeOffset.UtcNow)); // same product id, another store

        var result = await ListAsync();

        Assert.Equal([5], result.Value.Items.Select(movement => movement.Delta));
    }
}
