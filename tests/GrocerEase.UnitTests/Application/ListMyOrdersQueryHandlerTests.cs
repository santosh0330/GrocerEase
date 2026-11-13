using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Orders.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListMyOrdersQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeOrderRepository _orders = new();
    private readonly Guid _me = Guid.CreateVersion7();

    private Order Place(Guid customerId, int minutesAgo)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, Guid.CreateVersion7(), customerId, "1 High Street", Now.AddMinutes(-minutesAgo), [new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.5m, 1)]);
        _orders.Orders.Add(order);
        return order;
    }

    [Fact]
    public async Task OnlyTheCallersOrders_AreListed_NewestFirst_WithTheirLines()
    {
        var oldest = Place(_me, 30);
        var newest = Place(_me, 5);
        Place(Guid.CreateVersion7(), 1); // someone else's, and the newest of all

        var result = await new ListMyOrdersQueryHandler(_orders).Handle(new ListMyOrdersQuery(_me, 1, 20), CancellationToken.None);

        Assert.Equal([newest.Id, oldest.Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(2, result.Value.TotalCount);
        Assert.All(result.Value.Items, order => Assert.Single(order.Lines));
    }

    [Fact]
    public async Task APage_HoldsOnlyItsOrders_WithTheTotalOfAllOfThem()
    {
        var orders = Enumerable.Range(0, 5).Select(i => Place(_me, i)).ToList(); // placed 0, 1, 2, 3, 4 minutes ago

        var result = await new ListMyOrdersQueryHandler(_orders).Handle(new ListMyOrdersQuery(_me, 2, 2), CancellationToken.None);

        Assert.Equal([orders[2].Id, orders[3].Id], result.Value.Items.Select(order => order.Id));
        Assert.Equal(5, result.Value.TotalCount);
    }

    [Fact]
    public async Task ACustomerWithNoOrders_GetsAnEmptyPage_NotAnError()
    {
        var result = await new ListMyOrdersQueryHandler(_orders).Handle(new ListMyOrdersQuery(_me, 1, 20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }
}
