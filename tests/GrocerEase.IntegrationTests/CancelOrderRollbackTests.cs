using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Why cancelling an order is a transactional handler. The status change and each line's restored stock are separate statements, so when
/// the second line's stock cannot be put back, the status has already changed and the first line's stock is already back: unless the whole
/// thing is one unit, the order is cancelled and half its stock is missing, or the store is told "no" for an order that is cancelled
/// anyway. The real handler, repositories and unit of work run against PostgreSQL.
/// </summary>
public class CancelOrderRollbackTests
{
    private sealed record World(Guid StoreId, Guid StoreUserId, Guid CustomerId, Guid OrderId, Guid FirstId, Guid SecondId);

    /// <summary>A store with two products (the first by id holds 5, the second 1 after the order took 2 of each), a store user, and a pending order for 2 of each.</summary>
    private static async Task<World> AWorldAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var customer = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        var staff = new User(Guid.CreateVersion7(), "Sam", "Rai", $"sam-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.StoreUser);
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new UserRepository(db).TryAddAsync(customer, CancellationToken.None));
        Assert.True(await new UserRepository(db).TryAddAsync(staff, CancellationToken.None));
        Assert.True(await new StoreMemberRepository(db).TryAddAsync(new StoreMember(Guid.CreateVersion7(), store.Id, staff.Id), CancellationToken.None));
        var products = new[] { new Product(Guid.CreateVersion7(), store.Id, "Milk", "", 2.50m), new Product(Guid.CreateVersion7(), store.Id, "Bread", "", 1.10m) }
            .OrderBy(product => product.Id).ToArray();
        foreach (var product in products)
        {
            Assert.True(await new ProductRepository(db).TryAddAsync(product, CancellationToken.None));
        }

        var inventory = new InventoryItemRepository(db);
        Assert.Equal(5, await inventory.TryAdjustAsync(store.Id, products[0].Id, await StockActors.ChangeAsync(5), CancellationToken.None));
        Assert.Equal(1, await inventory.TryAdjustAsync(store.Id, products[1].Id, await StockActors.ChangeAsync(1), CancellationToken.None));

        var orderId = Guid.CreateVersion7();
        var order = Order.Place(
            orderId, store.Id, customer.Id, "1 High Street", DateTimeOffset.UtcNow,
            [new OrderLine(Guid.CreateVersion7(), orderId, products[0].Id, products[0].Name, 2.50m, 2), new OrderLine(Guid.CreateVersion7(), orderId, products[1].Id, products[1].Name, 1.10m, 2)]);
        await new OrderRepository(db).AddAsync(order, CancellationToken.None);
        return new World(store.Id, staff.Id, customer.Id, orderId, products[0].Id, products[1].Id);
    }

    private static async Task<Result<OrderResult>> CancelAsync(ServiceProvider provider, World world)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CancelOrderCommand, Result<OrderResult>>>()
            .Handle(new CancelOrderCommand(world.StoreId, world.OrderId, world.StoreUserId), CancellationToken.None);
    }

    private static async Task<(int? First, int? Second, string? Status)> StateAsync(World world)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            await PostgresServer.ScalarAsync<int?>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.FirstId}'"),
            await PostgresServer.ScalarAsync<int?>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.SecondId}'"),
            await PostgresServer.ScalarAsync<string?>(connection, $"select status from orders where id = '{world.OrderId}'"));
    }

    private static async Task<long> MovementsAsync(World world) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from stock_movements where store_id = '{world.StoreId}'");

    [Fact]
    public async Task WhenEveryLineCanBePutBack_TheOrderIsCancelledAndAllTheStockIsBack()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        var result = await CancelAsync(provider, world);

        Assert.True(result.IsSuccess);
        Assert.Equal((7, 3, "Cancelled"), await StateAsync(world));
        Assert.Equal(4L, await MovementsAsync(world)); // the setup's two, and one OrderCancelled movement for each line
    }

    [Fact]
    public async Task WhenTheSecondLinesStockCannotBePutBack_TheStatusAndTheFirstLinesStockGoBackToWhatTheyWere()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);
        // The store has since added stock of the second product right up to the limit, so putting 2 back would pass it.
        await PostgresServer.ExecuteAsync(
            await PostgresServer.ApplicationConnectionStringAsync(), $"update inventory_items set quantity_on_hand = {InventoryItem.MaxQuantity} where product_id = '{world.SecondId}'");

        var result = await CancelAsync(provider, world);

        // The first line (lower id) was put back (5 -> 7) and the status was already Cancelled when the second failed. Without the unit of
        // work that is what would remain: (7, max, "Cancelled"). With it, everything is as it was.
        Assert.Equal("Order.CannotRestoreStock", result.Error.Code);
        Assert.Equal((5, InventoryItem.MaxQuantity, "Pending"), await StateAsync(world));
        Assert.Equal(2L, await MovementsAsync(world)); // the first line's movement went back with its stock: only the setup's two remain
    }

    [Fact]
    public async Task TheCustomersCancellation_IsOneUnitToo_WhenTheSecondLinesStockCannotBePutBack_NothingChanges()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);
        await PostgresServer.ExecuteAsync(
            await PostgresServer.ApplicationConnectionStringAsync(), $"update inventory_items set quantity_on_hand = {InventoryItem.MaxQuantity} where product_id = '{world.SecondId}'");

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CancelMyOrderCommand, Result<OrderResult>>>()
            .Handle(new CancelMyOrderCommand(world.CustomerId, world.OrderId), CancellationToken.None);

        Assert.Equal("Order.CannotRestoreStock", result.Error.Code);
        Assert.Equal((5, InventoryItem.MaxQuantity, "Pending"), await StateAsync(world)); // the status and the first line's restore went back with it
    }

    [Fact]
    public async Task TheCustomersCancellation_PutsAllTheStockBack_WhenEveryLineCan()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CancelMyOrderCommand, Result<OrderResult>>>()
            .Handle(new CancelMyOrderCommand(world.CustomerId, world.OrderId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((7, 3, "Cancelled"), await StateAsync(world));
    }
}
