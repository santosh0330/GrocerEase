using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class CancelOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public CancelOrderCommandHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    /// <summary>An order for these (productId, quantity) lines, in the order given, stored in the status given. The stock of each product is what is left after it was reserved.</summary>
    private Order Place(OrderStatus status = OrderStatus.Pending, Guid? storeId = null, params (Guid ProductId, int Quantity)[] lines)
    {
        var id = Guid.CreateVersion7();
        var store = storeId ?? _storeId;
        var chosen = lines.Length == 0 ? [(Guid.CreateVersion7(), 2)] : lines;
        var order = Order.Place(id, store, Guid.CreateVersion7(), "1 High Street", Now, [.. chosen.Select(line => new OrderLine(Guid.CreateVersion7(), id, line.ProductId, "Milk", 2.5m, line.Quantity))]);
        _orders.Orders.Add(order);
        _orders.SomeoneElseMoves(id, status);
        foreach (var line in chosen)
        {
            _stock.Quantities[(store, line.ProductId)] = 10; // 10 left on the shelf
        }

        return order;
    }

    private Task<Result<OrderResult>> CancelAsync(Guid orderId, Guid? storeId = null, Guid? actorId = null) =>
        new CancelOrderCommandHandler(new StoreAccess(_members), _orders, _stock)
            .Handle(new CancelOrderCommand(storeId ?? _storeId, orderId, actorId ?? _userId), CancellationToken.None);

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Verified)]
    public async Task AnOrderThatHasNotShipped_IsCancelled_AndEveryLinesStockGoesBack(OrderStatus status)
    {
        var milk = Guid.CreateVersion7();
        var bread = Guid.CreateVersion7();
        var order = Place(status, null, (milk, 2), (bread, 3));

        var result = await CancelAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal((order.Id, "Cancelled", 2), (result.Value.Id, result.Value.Status, result.Value.Lines.Count));
        Assert.Equal(OrderStatus.Cancelled, _orders.StatusOf(order.Id));
        Assert.Equal((12, 13), (_stock.Quantities[(_storeId, milk)], _stock.Quantities[(_storeId, bread)])); // 10 + 2, 10 + 3
    }

    [Fact]
    public async Task TheStockIsPutBack_InOrderOfProductId_WhateverOrderTheLinesAreIn_SoTwoCancellationsCannotDeadlock()
    {
        var products = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var order = Place(OrderStatus.Pending, null, [.. products.OrderByDescending(id => id).Select(id => (id, 1))]); // stored in the opposite order

        await CancelAsync(order.Id);

        Assert.Equal(products.Order(), _stock.Calls.Select(call => call.ProductId));
        Assert.All(_stock.Calls, call => Assert.Equal(1, call.Delta)); // a restoration adds stock
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)] // the goods have left
    [InlineData(OrderStatus.Cancelled)] // already cancelled: its stock came back once
    public async Task AnOrderThatCannotBeCancelled_IsRefused_With409_AndNoStockMoves(OrderStatus status)
    {
        var order = Place(status);

        var result = await CancelAsync(order.Id);

        Assert.Equal("Order.InvalidTransition", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains(status.ToString(), result.Error.Description, StringComparison.Ordinal);
        Assert.Empty(_stock.Calls);
        Assert.Equal((status, 0), (_orders.StatusOf(order.Id), _orders.StatusChanges));
    }

    [Fact]
    public async Task WhenSomeoneElseCancelsOrShipsItFirst_ThisRequestPutsNothingBack()
    {
        // The loser must not restore: two cancellations would put the quantity back twice, and a shipped order's goods have left.
        foreach (var other in new[] { OrderStatus.Cancelled, OrderStatus.Shipped })
        {
            var order = Place();
            _orders.BeforeChangingStatus = () => _orders.SomeoneElseMoves(order.Id, other);

            var result = await CancelAsync(order.Id);

            Assert.Same(OrderErrors.ChangedMeanwhile, result.Error);
            Assert.Equal(other, _orders.StatusOf(order.Id));
        }

        Assert.Empty(_stock.Calls);
    }

    [Fact]
    public async Task WhenAQuantityCannotBePutBack_TheAnswerIs409NamingTheProduct()
    {
        var milk = Guid.CreateVersion7();
        var order = Place(OrderStatus.Pending, null, (milk, 2));
        _stock.Quantities[(_storeId, milk)] = InventoryItem.MaxQuantity; // the store has added stock up to the limit since

        var result = await CancelAsync(order.Id);

        Assert.Equal("Order.CannotRestoreStock", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains("'Milk'", result.Error.Description, StringComparison.Ordinal);
        Assert.Equal(InventoryItem.MaxQuantity, _stock.Quantities[(_storeId, milk)]);
        // That the status change is undone with it is the unit of work's job, and PlaceOrder's counterpart is tested against PostgreSQL.
    }

    [Fact]
    public async Task AnOrderOfAnotherStore_AnUnknownOne_AreTheSameOrderNotFound_AndNoStockMoves()
    {
        var elsewhere = Place(OrderStatus.Pending, Guid.CreateVersion7());

        var other = await CancelAsync(elsewhere.Id);
        var unknown = await CancelAsync(Guid.CreateVersion7());

        Assert.Same(OrderErrors.NotFound, other.Error);
        Assert.Same(OrderErrors.NotFound, unknown.Error);
        Assert.Empty(_stock.Calls);
        Assert.Equal(OrderStatus.Pending, _orders.StatusOf(elsewhere.Id));
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsStoreNotFound_AndNoStockMoves()
    {
        var order = Place();
        var notMine = Place(OrderStatus.Pending, Guid.CreateVersion7());

        var notAMember = await CancelAsync(order.Id, actorId: Guid.CreateVersion7());
        var otherStore = await CancelAsync(notMine.Id, storeId: notMine.StoreId);

        Assert.Same(StoreErrors.NotFound, notAMember.Error);
        Assert.Same(StoreErrors.NotFound, otherStore.Error);
        Assert.Empty(_stock.Calls);
        Assert.Equal(0, _orders.StatusChanges);
    }

    [Fact]
    public async Task EachLinesStockThatGoesBack_LeavesAnOrderCancelledMovement_ForThatOrder_ByTheStoreUserWhoCancelled()
    {
        var milk = Guid.CreateVersion7();
        var bread = Guid.CreateVersion7();
        var order = Place(OrderStatus.Verified, null, (milk, 2), (bread, 3));

        await CancelAsync(order.Id);

        Assert.All(_stock.Movements, movement => Assert.Equal((StockMovementReason.OrderCancelled, _userId, (Guid?)order.Id, _storeId), (movement.Reason, movement.ActorId, movement.OrderId, movement.StoreId)));
        Assert.Equal(new[] { (milk, 2), (bread, 3) }.OrderBy(pair => pair.Item1), _stock.Movements.Select(movement => (movement.ProductId, movement.Delta)).OrderBy(pair => pair.ProductId));
    }

    [Fact]
    public async Task ACancellationThatIsRefused_OrLostToSomeoneElse_LeavesNoMovement()
    {
        var shipped = Place(OrderStatus.Shipped);
        var raced = Place();
        _orders.BeforeChangingStatus = () => _orders.SomeoneElseMoves(raced.Id, OrderStatus.Cancelled);

        await CancelAsync(shipped.Id);
        await CancelAsync(raced.Id);

        Assert.Empty(_stock.Movements);
    }
}
