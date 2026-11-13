using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListStoreOrdersQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly FakeStoreMemberRepository _members = new();
    private readonly Guid _storeId = Guid.CreateVersion7();
    private readonly Guid _userId = Guid.CreateVersion7();

    public ListStoreOrdersQueryHandlerTests() =>
        _members.Members.Add(new StoreMember(Guid.CreateVersion7(), _storeId, _userId));

    private ListStoreOrdersQueryHandler Handler() => new(new StoreAccess(_members), _orders);

    private Order Place(int minutesAgo, Guid? storeId = null, OrderStatus status = OrderStatus.Pending)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, storeId ?? _storeId, Guid.CreateVersion7(), "1 High Street", Now.AddMinutes(-minutesAgo), [new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.5m, 1)]);
        if (status is OrderStatus.Verified or OrderStatus.Shipped)
        {
            Assert.True(order.Verify());
        }

        if (status is OrderStatus.Shipped)
        {
            Assert.True(order.Ship());
        }

        if (status is OrderStatus.Cancelled)
        {
            Assert.True(order.Cancel());
        }

        _orders.Orders.Add(order);
        return order;
    }

    private Task<Result<PagedResult<OrderResult>>> ListAsync(
        string? status = null, int page = 1, int pageSize = 20, Guid? storeId = null, Guid? actorId = null) =>
        Handler().Handle(new ListStoreOrdersQuery(storeId ?? _storeId, actorId ?? _userId, status, page, pageSize), CancellationToken.None);

    [Fact]
    public async Task TheOrdersOfTheStore_AreListedOldestFirst_WithTheirLines_AndNoOthers()
    {
        var newest = Place(5);
        var oldest = Place(30);
        Place(1, storeId: Guid.CreateVersion7()); // another store's

        var result = await ListAsync();

        Assert.Equal([oldest.Id, newest.Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(2, result.Value.TotalCount);
        Assert.All(result.Value.Items, order => Assert.Single(order.Lines));
    }

    [Fact]
    public async Task TheStatusFilter_KeepsOnlyThatStatus_InAnyCase_AndTheTotalCountsOnlyThose()
    {
        var pending = Place(10);
        var verified = Place(20, status: OrderStatus.Verified);
        Place(30, status: OrderStatus.Shipped);
        Place(40, status: OrderStatus.Cancelled);

        var onlyPending = await ListAsync("pending");
        var onlyVerified = await ListAsync("VERIFIED");
        var everything = await ListAsync();

        Assert.Equal([pending.Id], onlyPending.Value.Items.Select(order => order.Id));

        Assert.Equal(1, onlyPending.Value.TotalCount);
        Assert.Equal([verified.Id], onlyVerified.Value.Items.Select(order => order.Id));
        Assert.Equal(1, onlyVerified.Value.TotalCount);
        Assert.Equal(4, everything.Value.TotalCount);
    }

    [Fact]
    public async Task APage_HoldsOnlyItsOrders_WithTheTotalOfAllOfThem()
    {
        var orders = Enumerable.Range(0, 5).Select(i => Place(50 - i)).ToList(); // 50, 49, ... 46 minutes ago: oldest is orders[0]

        var result = await ListAsync(page: 2, pageSize: 2);

        Assert.Equal([orders[2].Id, orders[3].Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task AStoreTheCallerDoesNotRun_IsNotFound_LikeAStoreThatDoesNotExist_AndNothingIsRead()
    {
        Place(5, storeId: Guid.CreateVersion7());
        var otherStore = _orders.Orders[0].StoreId;

        var notMine = await ListAsync(storeId: otherStore);
        var unknown = await ListAsync(storeId: Guid.CreateVersion7());
        var notAMember = await ListAsync(actorId: Guid.CreateVersion7());

        Assert.All(new[] { notMine, unknown, notAMember }, result => Assert.Same(StoreErrors.NotFound, result.Error));
    }

    [Fact]
    public async Task AStoreWithNoOrders_GetsAnEmptyPage_NotAnError()
    {
        var result = await ListAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }
}
