using System.Runtime.CompilerServices;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Why a cancellation puts its lines' stock back in a fixed order. Two cancellations that share two products and take them in opposite
/// orders each hold one stock row and wait for the other: a deadlock, which PostgreSQL ends by failing one of them. A deadlock needs two
/// transactions to be between their two statements at the same instant, which thirty parallel requests almost never manage, so this test
/// <b>forces</b> it: each cancellation is held after its first restored line until the other has restored its first. If both start with
/// the same row, the second cannot get it, the hold times out and both finish; if they start with different rows, they deadlock.
/// </summary>
/// <remarks>
/// PostgreSQL happens to return an order's lines sorted by product id (it reads them through the unique index on <c>(order_id, product_id)</c>),
/// so with the real query alone the lines of every order arrive in the same order and the handler's own sort is never needed. Nothing
/// promises that, so the sort is the handler's, and this test takes the incidental order away: <see cref="OppositeOrders"/> hands the two
/// cancellations their lines in opposite orders, as a different query plan could.
/// </remarks>
public class CancelOrderDeadlockTests
{
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    /// <summary>The real inventory repository, except that after its first change in a unit of work it waits for the other unit's first change.</summary>
    private sealed class HoldingInventory(IInventoryItemRepository inner, Barrier barrier) : IInventoryItemRepository
    {
        private int _changes;

        public Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            inner.GetQuantitiesAsync(storeId, productIds, cancellationToken);

        public async Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken)
        {
            var result = await inner.TryAdjustAsync(storeId, productId, delta, cancellationToken);
            if (Interlocked.Increment(ref _changes) == 1)
            {
                // Both units now hold their first row (or, if they started with the same row, one is stuck waiting for it: then this times out).
                await Task.Run(() => barrier.SignalAndWait(Hold), cancellationToken);
            }

            return result;
        }
    }

    /// <summary>The real order repository, except that it hands out the lines of the first order read ascending and of the second descending.</summary>
    private sealed class OppositeOrders(IOrderRepository inner, StrongBox<int> reads) : IOrderRepository
    {
        public Task AddAsync(Order order, CancellationToken cancellationToken) => inner.AddAsync(order, cancellationToken);

        public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken) => inner.GetForCustomerAsync(customerId, orderId, cancellationToken);

        public Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListForCustomerAsync(customerId, page, pageSize, cancellationToken);

        public Task<PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListForStoreAsync(storeId, status, page, pageSize, cancellationToken);

        public Task<bool> TryChangeStatusAsync(Guid storeId, Guid orderId, OrderStatus expected, OrderStatus target, CancellationToken cancellationToken) =>
            inner.TryChangeStatusAsync(storeId, orderId, expected, target, cancellationToken);

        public async Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken)
        {
            if (await inner.GetForStoreAsync(storeId, orderId, cancellationToken) is not { } order)
            {
                return null;
            }

            Assert.Equal(OrderStatus.Pending, order.Status); // rebuilt below as a pending order
            var ascending = Interlocked.Increment(ref reads.Value) % 2 == 1;
            var lines = ascending ? order.Lines.OrderBy(line => line.ProductId) : order.Lines.OrderByDescending(line => line.ProductId);
            return Order.Place(order.Id, order.StoreId, order.CustomerId, order.DeliveryAddress, order.PlacedAt, lines);
        }
    }

    private sealed record World(Guid StoreId, Guid StaffId, Guid FirstOrderId, Guid SecondOrderId, Guid MilkId, Guid BreadId);

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
            Assert.Equal(10, await new InventoryItemRepository(db).TryAdjustAsync(store.Id, product.Id, 10, CancellationToken.None));
        }

        // The same two products, the lines stored in opposite orders.
        async Task<Guid> OrderAsync(params Product[] inOrder)
        {
            var id = Guid.CreateVersion7();
            var order = Order.Place(id, store.Id, customer.Id, "1 High Street", DateTimeOffset.UtcNow, [.. inOrder.Select(product => new OrderLine(Guid.CreateVersion7(), id, product.Id, product.Name, 1m, 1))]);
            await new OrderRepository(db).AddAsync(order, CancellationToken.None);
            return id;
        }

        var first = await OrderAsync(products[0], products[1]);
        var second = await OrderAsync(products[1], products[0]);
        return new World(store.Id, staff.Id, first, second, products[0].Id, products[1].Id);
    }

    private static async Task<Result<OrderResult>> CancelAsync(ServiceProvider provider, World world, Guid orderId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CancelOrderCommand, Result<OrderResult>>>()
            .Handle(new CancelOrderCommand(world.StoreId, orderId, world.StaffId), CancellationToken.None);
    }

    [Fact]
    public async Task TwoCancellationsWhoseLinesArriveInOppositeOrders_BothFinish_BecauseTheyTakeTheRowsInTheSameOrder()
    {
        var barrier = new Barrier(2);
        var reads = new StrongBox<int>();
        await using var provider = await RealApplication.BuildAsync(services =>
        {
            services.AddScoped<IInventoryItemRepository>(sp => new HoldingInventory(new InventoryItemRepository(sp.GetRequiredService<AppDbContext>()), barrier));
            services.AddScoped<IOrderRepository>(sp => new OppositeOrders(new OrderRepository(sp.GetRequiredService<AppDbContext>()), reads));
        });
        var world = await AWorldAsync(provider);

        var results = await Task.WhenAll(CancelAsync(provider, world, world.FirstOrderId), CancelAsync(provider, world, world.SecondOrderId));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code));
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        Assert.Equal(
            (12, 12),
            (await PostgresServer.ScalarAsync<int>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.MilkId}'"),
             await PostgresServer.ScalarAsync<int>(connection, $"select quantity_on_hand from inventory_items where product_id = '{world.BreadId}'")));
    }

    [Fact]
    public async Task TheTestItself_CanDeadlock_WhenTwoUnitsTakeTheRowsInOppositeOrders()
    {
        // The control: take the same two rows in opposite orders by hand, held at the same moment, and PostgreSQL ends one with a deadlock.
        // If this passed without one, the test above could not fail either, and would prove nothing.
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        await using var provider = await RealApplication.BuildAsync();
        var world = await AWorldAsync(provider);
        var barrier = new Barrier(2);

        async Task TakeAsync(Guid firstRow, Guid secondRow)
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var transaction = await db.BeginTransactionAsync();
            await using (var lockFirst = new NpgsqlCommand("update inventory_items set quantity_on_hand = quantity_on_hand + 1 where product_id = @id", db, transaction))
            {
                lockFirst.Parameters.AddWithValue("id", firstRow);
                await lockFirst.ExecuteNonQueryAsync();
            }

            await Task.Run(() => barrier.SignalAndWait(Hold));
            await using var lockSecond = new NpgsqlCommand("update inventory_items set quantity_on_hand = quantity_on_hand + 1 where product_id = @id", db, transaction);
            lockSecond.Parameters.AddWithValue("id", secondRow);
            await lockSecond.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        var attempts = await Task.WhenAll(
            Record(TakeAsync(world.MilkId, world.BreadId)),
            Record(TakeAsync(world.BreadId, world.MilkId)));

        Assert.Contains(attempts, exception => exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected });
    }

    private static async Task<Exception?> Record(Task work)
    {
        try
        {
            await work;
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }
}
