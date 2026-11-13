using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The order repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is. An order
/// and its lines are one aggregate; every read names the customer.
/// </summary>
public class OrderRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private sealed record World(Guid StoreId, Guid CustomerId, Guid MilkId, Guid BreadId);

    private static async Task<World> AWorldAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var customer = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        var milk = new Product(Guid.CreateVersion7(), store.Id, "Milk", "", 2.50m);
        var bread = new Product(Guid.CreateVersion7(), store.Id, "Bread", "", 1.10m);
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new UserRepository(db).TryAddAsync(customer, CancellationToken.None));
        Assert.True(await new ProductRepository(db).TryAddAsync(milk, CancellationToken.None));
        Assert.True(await new ProductRepository(db).TryAddAsync(bread, CancellationToken.None));
        return new World(store.Id, customer.Id, milk.Id, bread.Id);
    }

    private static async Task<Guid> NewCustomerAsync()
    {
        var customer = new User(Guid.CreateVersion7(), "Ben", "Rai", $"ben-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        await using var db = await NewContextAsync();
        Assert.True(await new UserRepository(db).TryAddAsync(customer, CancellationToken.None));
        return customer.Id;
    }

    private static async Task<Order> PlaceAsync(World world, Guid? customerId = null, DateTimeOffset? placedAt = null, int milk = 2, int bread = 1)
    {
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, world.StoreId, customerId ?? world.CustomerId, "1 High Street", placedAt ?? DateTimeOffset.UtcNow,
            [new OrderLine(Guid.CreateVersion7(), id, world.MilkId, "Milk", 2.50m, milk), new OrderLine(Guid.CreateVersion7(), id, world.BreadId, "Bread", 1.10m, bread)]);
        await using var db = await NewContextAsync();
        await new OrderRepository(db).AddAsync(order, CancellationToken.None);
        return order;
    }

    private static async Task<Order?> GetAsync(Guid customerId, Guid orderId)
    {
        await using var db = await NewContextAsync();
        return await new OrderRepository(db).GetForCustomerAsync(customerId, orderId, CancellationToken.None);
    }

    private static async Task<GrocerEase.Application.Common.PagedResult<Order>> ListAsync(Guid customerId, int page = 1, int pageSize = 20)
    {
        await using var db = await NewContextAsync();
        return await new OrderRepository(db).ListForCustomerAsync(customerId, page, pageSize, CancellationToken.None);
    }

    [Fact]
    public async Task AnOrder_IsStoredWithItsLines_AndReadBackWithAllOfThem()
    {
        var world = await AWorldAsync();
        var placed = await PlaceAsync(world);

        var found = await GetAsync(world.CustomerId, placed.Id);

        Assert.NotNull(found);
        Assert.Equal((placed.Id, world.StoreId, world.CustomerId, OrderStatus.Pending, "1 High Street"), (found.Id, found.StoreId, found.CustomerId, found.Status, found.DeliveryAddress));
        Assert.Equal(
            [("Bread", 1.10m, 1), ("Milk", 2.50m, 2)],
            found.Lines.OrderBy(line => line.ProductName, StringComparer.Ordinal).Select(line => (line.ProductName, line.UnitPrice, line.Quantity)));
        Assert.Equal(5.00m + 1.10m, found.Total);
        Assert.Equal(placed.PlacedAt.ToUniversalTime(), found.PlacedAt.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TheLinesKeepTheNameAndPriceTheyWereBoughtAt_WhateverHappensToTheProductAfterwards()
    {
        var world = await AWorldAsync();
        var placed = await PlaceAsync(world);
        var connection = await PostgresServer.ApplicationConnectionStringAsync();

        await PostgresServer.ExecuteAsync(connection, $"update products set name = 'Renamed', price = 99 where id = '{world.MilkId}'");
        await PostgresServer.ExecuteAsync(connection, $"update products set archived_at = now() where id = '{world.MilkId}'");

        var found = (await GetAsync(world.CustomerId, placed.Id))!;
        Assert.Equal(("Milk", 2.50m), found.Lines.Where(line => line.ProductId == world.MilkId).Select(line => (line.ProductName, line.UnitPrice)).Single());
    }

    [Fact]
    public async Task AnOrder_IsNotFoundThroughAnotherCustomer()
    {
        var world = await AWorldAsync();
        var placed = await PlaceAsync(world);

        Assert.Null(await GetAsync(await NewCustomerAsync(), placed.Id));
        Assert.Null(await GetAsync(world.CustomerId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task TheList_HoldsOnlyThatCustomersOrders_NewestFirst_WithTheirLines()
    {
        var world = await AWorldAsync();
        var other = await NewCustomerAsync();
        var oldest = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-3));
        var newest = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var middle = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-2));
        await PlaceAsync(world, customerId: other); // someone else's

        var page = await ListAsync(world.CustomerId);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], page.Items.Select(order => order.Id));
        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, order => Assert.Equal(2, order.Lines.Count));
    }

    [Fact]
    public async Task TheList_IsPaged_AndAPagePastTheEndIsEmpty()
    {
        var world = await AWorldAsync();
        var orders = new List<Order>();
        for (var i = 0; i < 5; i++)
        {
            orders.Add(await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }

        var second = await ListAsync(world.CustomerId, 2, 2);
        var past = await ListAsync(world.CustomerId, 9, 2);

        Assert.Equal([orders[2].Id, orders[3].Id], second.Items.Select(order => order.Id));
        Assert.Equal(5, second.TotalCount);
        Assert.Empty(past.Items);
        Assert.Equal(5, past.TotalCount);
    }

    [Fact]
    public async Task OrdersPlacedInTheSameInstant_KeepAStableOrder_BecauseTheIdBreaksTies()
    {
        var world = await AWorldAsync();
        var instant = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            ids.Add((await PlaceAsync(world, placedAt: instant)).Id);
        }

        var first = await ListAsync(world.CustomerId, 1, 2);
        var second = await ListAsync(world.CustomerId, 2, 2);

        Assert.Equal(ids.OrderByDescending(id => id), [.. first.Items.Select(order => order.Id), .. second.Items.Select(order => order.Id)]);
    }

    private static async Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId)
    {
        await using var db = await NewContextAsync();
        return await new OrderRepository(db).GetForStoreAsync(storeId, orderId, CancellationToken.None);
    }

    private static async Task<GrocerEase.Application.Common.PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status = null, int page = 1, int pageSize = 20)
    {
        await using var db = await NewContextAsync();
        return await new OrderRepository(db).ListForStoreAsync(storeId, status, page, pageSize, CancellationToken.None);
    }

    private static async Task SetStatusAsync(Order order, string status) =>
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = '{status}' where id = '{order.Id}'");

    [Fact]
    public async Task AnOrder_IsFoundInItsOwnStore_WithItsLines_AndNotThroughAnotherStore()
    {
        var world = await AWorldAsync();
        var elsewhere = await AWorldAsync();
        var placed = await PlaceAsync(world);

        var found = await GetForStoreAsync(world.StoreId, placed.Id);

        Assert.NotNull(found);
        Assert.Equal((placed.Id, world.CustomerId, 2), (found.Id, found.CustomerId, found.Lines.Count));
        Assert.Null(await GetForStoreAsync(elsewhere.StoreId, placed.Id));
        Assert.Null(await GetForStoreAsync(world.StoreId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task TheStoresList_HoldsOnlyThatStoresOrders_OldestFirst_FromEveryCustomer_WithTheirLines()
    {
        var world = await AWorldAsync();
        var elsewhere = await AWorldAsync();
        var other = await NewCustomerAsync();
        var newest = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var oldest = await PlaceAsync(world, customerId: other, placedAt: DateTimeOffset.UtcNow.AddHours(-3));
        var middle = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-2));
        await PlaceAsync(elsewhere); // another store's

        var page = await ListForStoreAsync(world.StoreId);

        Assert.Equal([oldest.Id, middle.Id, newest.Id], page.Items.Select(order => order.Id));
        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, order => Assert.Equal(2, order.Lines.Count));
    }

    [Fact]
    public async Task TheStatusFilter_KeepsOnlyThatStatus_AndTheTotalCountsOnlyThose()
    {
        var world = await AWorldAsync();
        var pending = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-1));
        var verified = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var shipped = await PlaceAsync(world, placedAt: DateTimeOffset.UtcNow.AddHours(-3));
        await SetStatusAsync(verified, "Verified");
        await SetStatusAsync(shipped, "Shipped");

        var onlyPending = await ListForStoreAsync(world.StoreId, OrderStatus.Pending);
        var onlyShipped = await ListForStoreAsync(world.StoreId, OrderStatus.Shipped);
        var noneCancelled = await ListForStoreAsync(world.StoreId, OrderStatus.Cancelled);

        Assert.Equal([pending.Id], onlyPending.Items.Select(order => order.Id));

        Assert.Equal(1, onlyPending.TotalCount);
        Assert.Equal([shipped.Id], onlyShipped.Items.Select(order => order.Id));
        Assert.Equal(1, onlyShipped.TotalCount);
        Assert.Equal((0, 0), (noneCancelled.Items.Count, noneCancelled.TotalCount));
        Assert.Equal(OrderStatus.Verified, (await GetForStoreAsync(world.StoreId, verified.Id))!.Status); // read back through the private setter
    }

    [Fact]
    public async Task TheStoresList_IsPaged_ByTheOldestFirst_AndTheIdBreaksTies()
    {
        var world = await AWorldAsync();
        var instant = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await PlaceAsync(world, placedAt: instant)).Id);
        }

        var pages = new[] { await ListForStoreAsync(world.StoreId, null, 1, 2), await ListForStoreAsync(world.StoreId, null, 2, 2), await ListForStoreAsync(world.StoreId, null, 3, 2) };

        Assert.Equal(ids.Order(), pages.SelectMany(page => page.Items.Select(order => order.Id)));
        Assert.All(pages, page => Assert.Equal(5, page.TotalCount));
        Assert.Empty((await ListForStoreAsync(world.StoreId, null, 9, 2)).Items);
    }

    private static async Task<bool> ChangeAsync(Guid storeId, Guid orderId, OrderStatus expected, OrderStatus target)
    {
        await using var db = await NewContextAsync();
        return await new OrderRepository(db).TryChangeStatusAsync(storeId, orderId, expected, target, CancellationToken.None);
    }

    [Fact]
    public async Task TheStatus_ChangesWhenItIsWhatWasExpected_AndNothingElseChanges()
    {
        var world = await AWorldAsync();
        var placed = await PlaceAsync(world);

        Assert.True(await ChangeAsync(world.StoreId, placed.Id, OrderStatus.Pending, OrderStatus.Verified));

        var found = (await GetForStoreAsync(world.StoreId, placed.Id))!;
        Assert.Equal(OrderStatus.Verified, found.Status);
        Assert.Equal((placed.DeliveryAddress, placed.CustomerId, 2), (found.DeliveryAddress, found.CustomerId, found.Lines.Count));
    }

    [Fact]
    public async Task TheStatus_IsLeftAlone_WhenItIsNotWhatWasExpected_WhenTheStoreIsAnotherOne_OrTheOrderIsUnknown()
    {
        var world = await AWorldAsync();
        var elsewhere = await AWorldAsync();
        var placed = await PlaceAsync(world);

        Assert.False(await ChangeAsync(world.StoreId, placed.Id, OrderStatus.Verified, OrderStatus.Shipped)); // it is Pending, not Verified
        Assert.False(await ChangeAsync(elsewhere.StoreId, placed.Id, OrderStatus.Pending, OrderStatus.Verified)); // through the wrong store
        Assert.False(await ChangeAsync(world.StoreId, Guid.CreateVersion7(), OrderStatus.Pending, OrderStatus.Verified));

        Assert.Equal(OrderStatus.Pending, (await GetForStoreAsync(world.StoreId, placed.Id))!.Status);
    }

    [Fact]
    public async Task OfManyCallersChangingTheSameOrderAtOnce_ExactlyOneWins()
    {
        var world = await AWorldAsync();
        var placed = await PlaceAsync(world);

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => ChangeAsync(world.StoreId, placed.Id, OrderStatus.Pending, OrderStatus.Verified))));

        Assert.Equal(1, results.Count(won => won));
        Assert.Equal(OrderStatus.Verified, (await GetForStoreAsync(world.StoreId, placed.Id))!.Status);
    }

    [Fact]
    public async Task AnOrderForAStoreThatDoesNotExist_IsAnException_NotAnOrdinaryRefusal()
    {
        var world = await AWorldAsync();
        var id = Guid.CreateVersion7();
        var order = Order.Place(
            id, Guid.CreateVersion7(), world.CustomerId, "x", DateTimeOffset.UtcNow, [new OrderLine(Guid.CreateVersion7(), id, world.MilkId, "Milk", 2.50m, 1)]);
        await using var db = await NewContextAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => new OrderRepository(db).AddAsync(order, CancellationToken.None));
    }
}
