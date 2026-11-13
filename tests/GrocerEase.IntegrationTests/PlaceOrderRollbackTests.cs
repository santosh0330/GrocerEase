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
        Assert.Equal(5, await inventory.TryAdjustAsync(store.Id, products[0].Id, 5, CancellationToken.None));
        Assert.Equal(1, await inventory.TryAdjustAsync(store.Id, products[1].Id, 1, CancellationToken.None));
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

    [Fact]
    public async Task WhenTheSecondLineCannotBeFilled_TheStockTheFirstLineTookGoesBack_AndNoOrderExists()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        // The first line takes 2 of 5 (and succeeds); the second asks for 2 of 1 (and cannot). Before the fix: 3 left and no order.
        var result = await PlaceAsync(provider, world, firstQuantity: 2, secondQuantity: 2);

        Assert.Equal("Order.InsufficientStock", result.Error.Code);
        Assert.Equal((5, 1, 0L, 0L), await StateAsync(world)); // nothing changed at all
    }

    [Fact]
    public async Task WhenEveryLineCanBeFilled_TheStockIsTaken_AndTheOrderAndItsLinesAreKeptTogether()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);

        var result = await PlaceAsync(provider, world, firstQuantity: 2, secondQuantity: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal((3, 0, 1L, 2L), await StateAsync(world));
    }

    [Fact]
    public async Task AnOrderThatFailsAtTheLastMoment_ForExampleWhenTheOrderCannotBeStored_TakesNoStock()
    {
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);
        // A customer who does not exist: the stock is reserved first and the order is stored last, and the foreign key then refuses the order.
        var command = new PlaceOrderCommand(world.StoreId, Guid.CreateVersion7(), "x", [new OrderLineInput(world.FirstId, 2)]);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<PlaceOrderCommand, Result<OrderResult>>>().Handle(command, CancellationToken.None));

        Assert.Equal((5, 1, 0L, 0L), await StateAsync(world)); // the 2 taken from the first product went back with the failed unit
    }
}
