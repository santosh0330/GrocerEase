using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ShipOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public ShipOrderCommandHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    private Order Place(OrderStatus status = OrderStatus.Verified, Guid? storeId = null)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, storeId ?? _storeId, Guid.CreateVersion7(), "1 High Street", Now, [new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.5m, 2)]);
        _orders.Orders.Add(order);
        _orders.SomeoneElseMoves(id, status); // where the order is stored, whatever Place made it
        return order;
    }

    private Task<Result<OrderResult>> ShipAsync(Guid orderId, Guid? storeId = null, Guid? actorId = null) =>
        new ShipOrderCommandHandler(new StoreAccess(_members), _orders)
            .Handle(new ShipOrderCommand(storeId ?? _storeId, orderId, actorId ?? _userId), CancellationToken.None);

    [Fact]
    public async Task AVerifiedOrder_IsShipped_StoredAsShipped_AndReturnedWithItsLines()
    {
        var order = Place();

        var result = await ShipAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal((order.Id, "Shipped", 5m), (result.Value.Id, result.Value.Status, result.Value.Total));
        Assert.Single(result.Value.Lines);
        Assert.Equal(OrderStatus.Shipped, _orders.StatusOf(order.Id));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)] // it has to be verified first
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task AnOrderThatIsNotVerified_IsRefused_With409AndTheStatusItIsIn_AndNothingIsWritten(OrderStatus status)
    {
        var order = Place(status);

        var result = await ShipAsync(order.Id);

        Assert.Equal("Order.InvalidTransition", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains(status.ToString(), result.Error.Description, StringComparison.Ordinal);
        Assert.Equal((status, 0), (_orders.StatusOf(order.Id), _orders.StatusChanges));
    }

    [Fact]
    public async Task WhenSomeoneCancelsItBetweenTheReadAndTheWrite_TheShipmentIsRefused_AndTheCancellationStands()
    {
        var order = Place();
        _orders.BeforeChangingStatus = () => _orders.SomeoneElseMoves(order.Id, OrderStatus.Cancelled);

        var result = await ShipAsync(order.Id);

        Assert.Same(OrderErrors.ChangedMeanwhile, result.Error);
        Assert.Equal((OrderStatus.Cancelled, 0), (_orders.StatusOf(order.Id), _orders.StatusChanges));
    }

    [Fact]
    public async Task AnOrderOfAnotherStore_AnUnknownOne_AreTheSameOrderNotFound()
    {
        var elsewhere = Place(storeId: Guid.CreateVersion7());

        var other = await ShipAsync(elsewhere.Id);
        var unknown = await ShipAsync(Guid.CreateVersion7());

        Assert.Same(OrderErrors.NotFound, other.Error);
        Assert.Same(OrderErrors.NotFound, unknown.Error);
        Assert.Equal(OrderStatus.Verified, _orders.StatusOf(elsewhere.Id));
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsStoreNotFound_AndNothingIsWritten()
    {
        var order = Place();
        var notMine = Place(storeId: Guid.CreateVersion7());

        var notAMember = await ShipAsync(order.Id, actorId: Guid.CreateVersion7());
        var otherStore = await ShipAsync(notMine.Id, storeId: notMine.StoreId);

        Assert.Same(StoreErrors.NotFound, notAMember.Error);
        Assert.Same(StoreErrors.NotFound, otherStore.Error);
        Assert.Equal(0, _orders.StatusChanges);
    }
}
