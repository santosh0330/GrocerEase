using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The admin's read of every store's orders against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is.
/// The database is shared with other tests, so a test that cares about an exact page narrows it to its own store.
/// </summary>
public class AdminOrderReaderTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private sealed record World(Guid StoreId, Guid CustomerId, Guid MilkId);

    private static async Task<World> AWorldAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var customer = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        var milk = new Product(Guid.CreateVersion7(), store.Id, "Milk", "", 2.50m);
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new UserRepository(db).TryAddAsync(customer, CancellationToken.None));
        Assert.True(await new ProductRepository(db).TryAddAsync(milk, CancellationToken.None));
        return new World(store.Id, customer.Id, milk.Id);
    }

    private static async Task<Order> PlaceAsync(World world, DateTimeOffset? placedAt = null, OrderStatus status = OrderStatus.Pending)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(id, world.StoreId, world.CustomerId, "1 High Street", placedAt ?? DateTimeOffset.UtcNow, [new OrderLine(Guid.CreateVersion7(), id, world.MilkId, "Milk", 2.50m, 2)]);
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

        await using var db = await NewContextAsync();
        await new OrderRepository(db).AddAsync(order, CancellationToken.None);
        return order;
    }

    private static async Task<PagedResult<Order>> ListAsync(Guid? storeId = null, OrderStatus? status = null, int page = 1, int pageSize = 20)
    {
        await using var db = await NewContextAsync();
        return await new AdminOrderReader(db).ListAsync(storeId, status, page, pageSize, CancellationToken.None);
    }

    [Fact]
    public async Task WithNoFilter_TheOrdersOfEveryStoreAreListed_NewestFirst_WithTheirLines()
    {
        var (a, b) = (await AWorldAsync(), await AWorldAsync());
        // Far enough in the future that no other test's order is newer, so these two lead the first page.
        var older = await PlaceAsync(a, DateTimeOffset.UtcNow.AddDays(400));
        var newer = await PlaceAsync(b, DateTimeOffset.UtcNow.AddDays(401));

        var page = await ListAsync();

        Assert.Equal([newer.Id, older.Id], page.Items.Take(2).Select(order => order.Id));
        Assert.Equal([b.StoreId, a.StoreId], page.Items.Take(2).Select(order => order.StoreId));
        Assert.All(page.Items.Take(2), order => Assert.Equal(("Milk", 2), (order.Lines.Single().ProductName, order.Lines.Single().Quantity)));
        Assert.True(page.TotalCount >= 2);
    }

    [Fact]
    public async Task TheStoreFilter_KeepsOnlyThatStore_NewestFirst_AndTheTotalCountsOnlyItsOrders()
    {
        var (a, b) = (await AWorldAsync(), await AWorldAsync());
        var oldest = await PlaceAsync(a, DateTimeOffset.UtcNow.AddHours(-3));
        var newest = await PlaceAsync(a, DateTimeOffset.UtcNow.AddHours(-1));
        var middle = await PlaceAsync(a, DateTimeOffset.UtcNow.AddHours(-2));
        await PlaceAsync(b); // another store's

        var page = await ListAsync(a.StoreId);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], page.Items.Select(order => order.Id));
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task TheStatusFilter_KeepsOnlyThatStatus_AndCombinesWithTheStore()
    {
        var (a, b) = (await AWorldAsync(), await AWorldAsync());
        var pending = await PlaceAsync(a);
        var verified = await PlaceAsync(a, status: OrderStatus.Verified);
        await PlaceAsync(a, status: OrderStatus.Shipped);
        await PlaceAsync(b, status: OrderStatus.Verified);

        var onlyPending = await ListAsync(a.StoreId, OrderStatus.Pending);
        var verifiedInA = await ListAsync(a.StoreId, OrderStatus.Verified);
        var verifiedAnywhere = await ListAsync(status: OrderStatus.Verified, pageSize: 100);

        Assert.Equal([pending.Id], onlyPending.Items.Select(order => order.Id));
        Assert.Equal(1, onlyPending.TotalCount);
        Assert.Equal([verified.Id], verifiedInA.Items.Select(order => order.Id));
        Assert.All(verifiedAnywhere.Items, order => Assert.Equal(OrderStatus.Verified, order.Status));
        Assert.Contains(verified.Id, verifiedAnywhere.Items.Select(order => order.Id));
    }

    [Fact]
    public async Task AStoreWithNoOrders_AndAStoreThatDoesNotExist_AreEmptyPages()
    {
        var empty = await AWorldAsync();

        var none = await ListAsync(empty.StoreId);
        var unknown = await ListAsync(Guid.CreateVersion7());

        Assert.All(new[] { none, unknown }, page => Assert.Equal((0, 0), (page.Items.Count, page.TotalCount)));
    }

    [Fact]
    public async Task APage_HoldsOnlyItsOrders_WithTheTotalOfAllOfThem_AndAPagePastTheEndIsEmpty()
    {
        var world = await AWorldAsync();
        var placed = new List<Order>();
        for (var i = 0; i < 5; i++)
        {
            placed.Add(await PlaceAsync(world, DateTimeOffset.UtcNow.AddHours(-10 + i))); // placed[4] is the newest
        }

        var second = await ListAsync(world.StoreId, page: 2, pageSize: 2);
        var past = await ListAsync(world.StoreId, page: 4, pageSize: 2);

        Assert.Equal([placed[2].Id, placed[1].Id], second.Items.Select(order => order.Id));
        Assert.Equal((2, 2, 5), (second.Page, second.PageSize, second.TotalCount));
        Assert.Empty(past.Items);
        Assert.Equal(5, past.TotalCount);
    }
}
