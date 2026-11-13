using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class CancelMyOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _me = Guid.CreateVersion7();

    /// <summary>An order of <paramref name="customerId"/> (me by default) for these lines, stored in this status; each product has 10 left on the shelf.</summary>
    private Order Place(OrderStatus status = OrderStatus.Pending, Guid? customerId = null, params (Guid ProductId, int Quantity)[] lines)
    {
        var id = Guid.CreateVersion7();
        var chosen = lines.Length == 0 ? [(Guid.CreateVersion7(), 2)] : lines;
        var order = Order.Place(
            id, _storeId, customerId ?? _me, "1 High Street", Now, [.. chosen.Select(line => new OrderLine(Guid.CreateVersion7(), id, line.ProductId, "Milk", 2.5m, line.Quantity))]);
        _orders.Orders.Add(order);
        _orders.SomeoneElseMoves(id, status);
        foreach (var line in chosen)
        {
            _stock.Quantities[(_storeId, line.ProductId)] = 10;
        }

        return order;
    }

    private Task<Result<OrderResult>> CancelAsync(Guid orderId, Guid? customerId = null) =>
        new CancelMyOrderCommandHandler(_orders, _stock).Handle(new CancelMyOrderCommand(customerId ?? _me, orderId), CancellationToken.None);

    [Fact]
    public async Task APendingOrderOfTheCaller_IsCancelled_AndEveryLinesStockGoesBack()
    {
        var milk = Guid.CreateVersion7();
        var bread = Guid.CreateVersion7();
        var order = Place(OrderStatus.Pending, null, (milk, 2), (bread, 3));

        var result = await CancelAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal((order.Id, "Cancelled", 2), (result.Value.Id, result.Value.Status, result.Value.Lines.Count));
        Assert.Equal(OrderStatus.Cancelled, _orders.StatusOf(order.Id));
        Assert.Equal((12, 13), (_stock.Quantities[(_storeId, milk)], _stock.Quantities[(_storeId, bread)]));
    }

    [Theory]
    [InlineData(OrderStatus.Verified)] // the store has accepted it: only the store can cancel now
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task AnOrderThatIsNoLongerPending_IsRefused_With409AndTheStatusItIsIn_AndNoStockMoves(OrderStatus status)
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
    public async Task WhenTheStoreVerifiesItBetweenTheReadAndTheWrite_TheCustomerLoses_AndPutsNothingBack()
    {
        // The order was Pending when the customer's request read it; the store verified it before the customer's write. A check followed by
        // a write would let the cancellation through and put the stock of a verified order back on the shelf.
        var order = Place();
        _orders.BeforeChangingStatus = () => _orders.SomeoneElseMoves(order.Id, OrderStatus.Verified);

        var result = await CancelAsync(order.Id);

        Assert.Same(OrderErrors.ChangedMeanwhile, result.Error);
        Assert.Equal((OrderStatus.Verified, 0), (_orders.StatusOf(order.Id), _orders.StatusChanges));
        Assert.Empty(_stock.Calls);
    }

    [Fact]
    public async Task SomeoneElsesOrder_AndAnUnknownOne_AreTheSameOrderNotFound_AndNothingMoves()
    {
        var theirs = Place(OrderStatus.Pending, Guid.CreateVersion7());

        var someoneElses = await CancelAsync(theirs.Id);
        var unknown = await CancelAsync(Guid.CreateVersion7());

        Assert.Same(OrderErrors.NotFound, someoneElses.Error);
        Assert.Same(OrderErrors.NotFound, unknown.Error);
        Assert.Empty(_stock.Calls);
        Assert.Equal((OrderStatus.Pending, 0), (_orders.StatusOf(theirs.Id), _orders.StatusChanges));
    }

    [Fact]
    public async Task TheStockIsPutBack_InOrderOfProductId_WhateverOrderTheLinesAreIn()
    {
        var products = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var order = Place(OrderStatus.Pending, null, [.. products.OrderByDescending(id => id).Select(id => (id, 1))]);

        await CancelAsync(order.Id);

        Assert.Equal(products.Order(), _stock.Calls.Select(call => call.ProductId));
    }

    [Fact]
    public async Task WhenAQuantityCannotBePutBack_TheAnswerIs409NamingTheProduct()
    {
        var milk = Guid.CreateVersion7();
        var order = Place(OrderStatus.Pending, null, (milk, 2));
        _stock.Quantities[(_storeId, milk)] = InventoryItem.MaxQuantity;

        var result = await CancelAsync(order.Id);

        Assert.Equal("Order.CannotRestoreStock", result.Error.Code);
        Assert.Contains("'Milk'", result.Error.Description, StringComparison.Ordinal);
    }
}
