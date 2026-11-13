using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class VerifyOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public VerifyOrderCommandHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    private Order Place(Guid? storeId = null)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, storeId ?? _storeId, Guid.CreateVersion7(), "1 High Street", Now, [new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.5m, 2)]);
        _orders.Orders.Add(order);
        return order;
    }

    private Task<Result<OrderResult>> VerifyAsync(Guid orderId, Guid? storeId = null, Guid? actorId = null) =>
        new VerifyOrderCommandHandler(new StoreAccess(_members), _orders)
            .Handle(new VerifyOrderCommand(storeId ?? _storeId, orderId, actorId ?? _userId), CancellationToken.None);

    [Fact]
    public async Task APendingOrder_IsVerified_StoredAsVerified_AndReturnedWithItsLines()
    {
        var order = Place();

        var result = await VerifyAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal((order.Id, "Verified", 5m), (result.Value.Id, result.Value.Status, result.Value.Total));
        Assert.Single(result.Value.Lines);
        Assert.Equal(OrderStatus.Verified, _orders.StatusOf(order.Id));
    }

    [Theory]
    [InlineData(OrderStatus.Verified)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task AnOrderThatIsNotPending_IsRefused_With409AndTheStatusItIsIn_AndNothingIsWritten(OrderStatus status)
    {
        var order = Place();
        _orders.SomeoneElseMoves(order.Id, status);

        var result = await VerifyAsync(order.Id);

        Assert.Equal("Order.InvalidTransition", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains(status.ToString(), result.Error.Description, StringComparison.Ordinal);
        Assert.Equal((status, 0), (_orders.StatusOf(order.Id), _orders.StatusChanges));
    }

    [Fact]
    public async Task WhenSomeoneVerifiesItBetweenTheReadAndTheWrite_TheLoserGetsChangedMeanwhile_AndChangesNothing()
    {
        var order = Place();
        _orders.BeforeChangingStatus = () => _orders.SomeoneElseMoves(order.Id, OrderStatus.Verified);

        var result = await VerifyAsync(order.Id);

        Assert.Same(OrderErrors.ChangedMeanwhile, result.Error);
        Assert.Equal(0, _orders.StatusChanges); // this request wrote nothing: the other one's move stands
    }

    [Fact]
    public async Task AnOrderOfAnotherStore_AnUnknownOne_AreTheSameOrderNotFound()
    {
        var elsewhere = Place(Guid.CreateVersion7());

        var other = await VerifyAsync(elsewhere.Id); // through MY store's route
        var unknown = await VerifyAsync(Guid.CreateVersion7());

        Assert.Same(OrderErrors.NotFound, other.Error);
        Assert.Same(OrderErrors.NotFound, unknown.Error);
        Assert.Equal(OrderStatus.Pending, _orders.StatusOf(elsewhere.Id));
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsStoreNotFound_AndTheOrderIsNotLookedAt()
    {
        var order = Place();
        var notMine = Place(Guid.CreateVersion7());

        var notAMember = await VerifyAsync(order.Id, actorId: Guid.CreateVersion7());
        var otherStore = await VerifyAsync(notMine.Id, storeId: notMine.StoreId);

        Assert.Same(StoreErrors.NotFound, notAMember.Error);
        Assert.Same(StoreErrors.NotFound, otherStore.Error);
        Assert.Equal((OrderStatus.Pending, OrderStatus.Pending, 0), (_orders.StatusOf(order.Id), _orders.StatusOf(notMine.Id), _orders.StatusChanges));
    }
}
