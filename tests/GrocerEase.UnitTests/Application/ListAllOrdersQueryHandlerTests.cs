using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListAllOrdersQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeAdminOrderReader _reader = new();
    private readonly Guid _storeA = Guid.CreateVersion7();
    private readonly Guid _storeB = Guid.CreateVersion7();

    private Order Place(int minutesAgo, Guid storeId, OrderStatus status = OrderStatus.Pending)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, storeId, Guid.CreateVersion7(), "1 High Street", Now.AddMinutes(-minutesAgo), [new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.5m, 1)]);
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

        _reader.Orders.Add(order);
        return order;
    }

    private Task<Result<PagedResult<OrderResult>>> ListAsync(Guid? storeId = null, string? status = null, int page = 1, int pageSize = 20) =>
        new ListAllOrdersQueryHandler(_reader).Handle(new ListAllOrdersQuery(storeId, status, page, pageSize), CancellationToken.None);

    [Fact]
    public async Task TheOrdersOfEveryStore_AreListedNewestFirst_WithTheirLines()
    {
        var oldest = Place(30, _storeA);
        var newest = Place(5, _storeB);
        var middle = Place(10, _storeA);

        var result = await ListAsync();

        Assert.Equal([newest.Id, middle.Id, oldest.Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal([_storeB, _storeA, _storeA], result.Value.Items.Select(order => order.StoreId));
        Assert.All(result.Value.Items, order => Assert.Single(order.Lines));
    }

    [Fact]
    public async Task TheStoreFilter_KeepsOnlyThatStore_AndTheTotalCountsOnlyItsOrders()
    {
        var inA = Place(10, _storeA);
        Place(5, _storeB);

        var result = await ListAsync(storeId: _storeA);

        Assert.Equal([inA.Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task AStoreThatDoesNotExist_IsAnEmptyPage_NotAnError()
    {
        Place(5, _storeA);

        var result = await ListAsync(storeId: Guid.CreateVersion7());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task TheStatusFilter_KeepsOnlyThatStatus_InAnyCase_AcrossStores()
    {
        var pendingA = Place(10, _storeA);
        var pendingB = Place(20, _storeB);
        Place(30, _storeA, OrderStatus.Verified);
        Place(40, _storeB, OrderStatus.Shipped);
        Place(50, _storeA, OrderStatus.Cancelled);

        var pending = await ListAsync(status: "pending");
        var verified = await ListAsync(status: "VERIFIED");

        Assert.Equal([pendingA.Id, pendingB.Id], pending.Value.Items.Select(order => order.Id));
        Assert.Equal(2, pending.Value.TotalCount);
        Assert.Equal(1, verified.Value.TotalCount);
    }

    [Fact]
    public async Task TheStoreAndStatusFilters_Combine()
    {
        var wanted = Place(10, _storeA, OrderStatus.Verified);
        Place(20, _storeA);
        Place(30, _storeB, OrderStatus.Verified);

        var result = await ListAsync(_storeA, "Verified");

        Assert.Equal([wanted.Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task APage_HoldsOnlyItsOrders_WithTheTotalOfAllOfThem()
    {
        var orders = Enumerable.Range(0, 5).Select(i => Place(50 - i, i % 2 == 0 ? _storeA : _storeB)).ToList(); // orders[4] is the newest

        var result = await ListAsync(page: 2, pageSize: 2);

        Assert.Equal([orders[2].Id, orders[1].Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task NoOrdersAtAll_IsAnEmptyPage()
    {
        var result = await ListAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }
}
