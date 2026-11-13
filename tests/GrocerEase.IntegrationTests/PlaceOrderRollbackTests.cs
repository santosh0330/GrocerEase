using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Why placing an order is a transactional handler. Each line reserves its stock with its own statement, so when the second line cannot be
/// filled, the first has already taken its stock: unless the whole thing is one unit, the customer gets an error and the store has lost
/// stock to an order that does not exist. The real handler, repositories and unit of work run against PostgreSQL.
/// </summary>
public class PlaceOrderRollbackTests
{
    private sealed record World(Guid StoreId, Guid CustomerId, Guid FirstId, Guid SecondId);

    /// <summary>A store with two products, the first (by id) holding 5 and the second 1, and a customer.</summary>
    private static async Task<World> AWorldAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var customer = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new UserRepository(db).TryAddAsync(customer, CancellationToken.None));
        var products = new[] { new Product(Guid.CreateVersion7(), store.Id, "Milk", "", 2.50m), new Product(Guid.CreateVersion7(), store.Id, "Bread", "", 1.10m) }
            .OrderBy(product => product.Id).ToArray(); // the handler reserves in this order
        foreach (var product in products)
        {
            Assert.True(await new ProductRepository(db).TryAddAsync(product, CancellationToken.None));
        }

        var inventory = new InventoryItemRepository(db);
        Assert.Equal(5, await inventory.TryAdjustAsync(store.Id, products[0].Id, await StockActors.ChangeAsync(5), CancellationToken.None));
        Assert.Equal(1, await inventory.TryAdjustAsync(store.Id, products[1].Id, await StockActors.ChangeAsync(1), CancellationToken.None));
        return new World(store.Id, customer.Id, products[0].Id, products[1].Id);
    }

    private static async Task<Result<OrderResult>> PlaceAsync(ServiceProvider provider, World world, int firstQuantity, int secondQuantity)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrderCommand, Result<OrderResult>>>().Handle(
            new PlaceOrderCommand(
                world.StoreId, world.CustomerId, "1 High Street",
                [new OrderLineInput(world.FirstId, firstQuantity), new OrderLineInput(world.SecondId, secondQuantity)]),
            CancellationToken.None);
    }

    private static async Task<(int? First, int? Second, long Orders, long Lines)> StateAsync(World world)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            await PostgresServer.ScalarAsync<int?>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.FirstId}'"),
            await PostgresServer.ScalarAsync<int?>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.SecondId}'"),
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from orders where customer_id = '{world.CustomerId}'"),
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from order_lines l join orders o on o.id = l.order_id where o.customer_id = '{world.CustomerId}'"));
    }

    private static async Task<long> MovementsAsync(World world) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from stock_movements where store_id = '{world.StoreId}'");

    [Fact]
    public async Task WhenTheSecondLineCannotBeFilled_TheStockTheFirstLineTookGoesBack_AndNoOrderExists()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        // The first line takes 2 of 5 (and succeeds); the second asks for 2 of 1 (and cannot). Before the fix: 3 left and no order.
        var result = await PlaceAsync(provider, world, firstQuantity: 2, secondQuantity: 2);

        Assert.Equal("Order.InsufficientStock", result.Error.Code);
        Assert.Equal((5, 1, 0L, 0L), await StateAsync(world)); // nothing changed at all
        Assert.Equal(2L, await MovementsAsync(world)); // the first line's movement went back with its stock: only the setup's two remain
    }

    [Fact]
    public async Task WhenEveryLineCanBeFilled_TheStockIsTaken_AndTheOrderAndItsLinesAreKeptTogether()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        var result = await PlaceAsync(provider, world, firstQuantity: 2, secondQuantity: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal((3, 0, 1L, 2L), await StateAsync(world));
        Assert.Equal(4L, await MovementsAsync(world)); // the setup's two, and one movement for each of the two lines
    }

    /// <summary>The real order repository, except that the order cannot be stored: whatever the reason, it is an exception after the stock was taken.</summary>
    private sealed class OrdersThatCannotBeStored(IOrderRepository inner) : IOrderRepository
    {
        public Task AddAsync(Order order, CancellationToken cancellationToken) => throw new InvalidOperationException("The order cannot be stored.");

        public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken) => inner.GetForCustomerAsync(customerId, orderId, cancellationToken);

        public Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken) => inner.GetForStoreAsync(storeId, orderId, cancellationToken);

        public Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListForCustomerAsync(customerId, page, pageSize, cancellationToken);

        public Task<PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListForStoreAsync(storeId, status, page, pageSize, cancellationToken);

        public Task<bool> TryChangeStatusAsync(Guid storeId, Guid orderId, OrderStatus expected, OrderStatus target, CancellationToken cancellationToken) =>
            inner.TryChangeStatusAsync(storeId, orderId, expected, target, cancellationToken);
    }

    [Fact]
    public async Task AnOrderThatCannotBeStoredAfterItsStockWasTaken_TakesNoStock_AndLeavesNoMovements()
    {
        await using var provider = await RealApplication.BuildAsync(services =>
            services.AddScoped<IOrderRepository>(sp => new OrdersThatCannotBeStored(new OrderRepository(sp.GetRequiredService<AppDbContext>()))));
        var world = await AWorldAsync(provider);
        var command = new PlaceOrderCommand(world.StoreId, world.CustomerId, "x", [new OrderLineInput(world.FirstId, 2), new OrderLineInput(world.SecondId, 1)]);
        using var scope = provider.CreateScope();

        // Both lines are reserved (the quantities change, and each change writes its movement), and then the order cannot be stored.
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrderCommand, Result<OrderResult>>>().Handle(command, CancellationToken.None));

        Assert.Equal((5, 1, 0L, 0L), await StateAsync(world)); // the stock went back with the failed unit...
        Assert.Equal(2L, await MovementsAsync(world)); // ...and so did the two movements: only the setup's two deliveries remain
    }

    [Fact]
    public async Task ACustomerWhoDoesNotExist_IsRefusedByTheLedgersForeignKey_AtTheFirstReservation_AndTakesNoStock()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);
        // The customer is the actor of the movement, and the ledger's foreign key says an actor is a real user. The statement that takes the
        // stock and writes the movement is one, so it fails as one: the quantity does not move.
        var command = new PlaceOrderCommand(world.StoreId, Guid.CreateVersion7(), "x", [new OrderLineInput(world.FirstId, 2)]);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrderCommand, Result<OrderResult>>>().Handle(command, CancellationToken.None));

        Assert.Equal("fk_stock_movements_users_actor_id", exception.ConstraintName);
        Assert.Equal((5, 1, 0L, 0L), await StateAsync(world));
        Assert.Equal(2L, await MovementsAsync(world));
    }
}
