using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The stock ledger against a real PostgreSQL, connected as the least-privilege application role. The point of the design is that the
/// quantity and its movement are written by one statement, so the ledger adds up whatever happens: refused changes, many requests at once,
/// a failing transaction around them.
/// </summary>
public class StockLedgerRepositoryTests
{
    private sealed record Row(int Delta, string Reason, Guid? ActorId, Guid? OrderId, DateTimeOffset CreatedAt);

    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static async Task<(Guid StoreId, Guid ProductId)> AProductAsync(string name = "Milk")
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var product = new Product(Guid.CreateVersion7(), store.Id, name, "", 2.5m);
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new ProductRepository(db).TryAddAsync(product, CancellationToken.None));
        return (store.Id, product.Id);
    }

    private static async Task<int?> ChangeAsync(Guid storeId, Guid productId, StockChange change, TimeProvider? clock = null)
    {
        await using var db = await NewContextAsync();
        return await new InventoryItemRepository(db, clock).TryAdjustAsync(storeId, productId, change, CancellationToken.None);
    }

    private static async Task<List<Row>> MovementsAsync(Guid productId)
    {
        await using var db = await NewContextAsync();
        var rows = await db.StockMovements.AsNoTracking().Where(movement => movement.ProductId == productId).OrderBy(movement => movement.Id).ToListAsync();
        return [.. rows.Select(movement => new Row(movement.Delta, movement.Reason.ToString(), movement.ActorId, movement.OrderId, movement.CreatedAt))];
    }

    private static async Task<int?> QuantityAsync(Guid productId) =>
        await PostgresServer.ScalarAsync<int?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select quantity_on_hand from inventory_items where product_id = '{productId}'");

    [Fact]
    public async Task AChange_WritesItsMovement_WithTheAmountTheReasonTheActorAndTheTimeOfTheClock()
    {
        var (store, milk) = await AProductAsync();
        var actor = await StockActors.IdAsync();
        var clock = new AdjustableTimeProvider();

        Assert.Equal(12, await ChangeAsync(store, milk, new StockChange(12, StockMovementReason.Received, actor), clock));

        var movement = Assert.Single(await MovementsAsync(milk));
        Assert.Equal((12, "Received", (Guid?)actor, (Guid?)null), (movement.Delta, movement.Reason, movement.ActorId, movement.OrderId));
        Assert.Equal(clock.GetUtcNow().ToUniversalTime(), movement.CreatedAt.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task TheFirstAdd_MakesTheRowAndItsMovementTogether_AndLaterChangesAddToThem()
    {
        var (store, milk) = await AProductAsync();

        await ChangeAsync(store, milk, await StockActors.ChangeAsync(10));
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(-3));
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(5));

        Assert.Equal([10, -3, 5], (await MovementsAsync(milk)).Select(row => row.Delta));
        Assert.Equal(12, await QuantityAsync(milk));
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task ARefusedChange_WritesNoMovement_WhetherItWouldGoBelowZeroOrAboveTheMaximum_OrIsForAProductWithNoRow()
    {
        var (store, milk) = await AProductAsync();
        var (_, bread) = await AProductAsync("Bread");
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(5));

        Assert.Null(await ChangeAsync(store, milk, await StockActors.ChangeAsync(-6))); // below zero
        Assert.Null(await ChangeAsync(store, milk, await StockActors.ChangeAsync(InventoryItem.MaxQuantity))); // past the limit
        Assert.Null(await ChangeAsync(store, bread, await StockActors.ChangeAsync(-1))); // never had stock

        Assert.Equal([5], (await MovementsAsync(milk)).Select(row => row.Delta));
        Assert.Empty(await MovementsAsync(bread));
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task AChangeThroughAnotherStore_WritesNoMovement_WhenTheRowExists()
    {
        var (store, milk) = await AProductAsync();
        var (other, _) = await AProductAsync();
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(7));

        Assert.Null(await ChangeAsync(other, milk, await StockActors.ChangeAsync(3)));
        Assert.Null(await ChangeAsync(other, milk, await StockActors.ChangeAsync(-3)));

        Assert.Equal([7], (await MovementsAsync(milk)).Select(row => row.Delta));
        Assert.Equal(7, await QuantityAsync(milk));
    }

    [Fact]
    public async Task ALevelSetForAProductWithNoRow_MakesTheRowAtZero_WithNothingToExplain()
    {
        var (store, milk) = await AProductAsync();
        await using var db = await NewContextAsync();

        await new InventoryItemRepository(db).SetLowStockThresholdAsync(store, milk, 5, CancellationToken.None);

        Assert.Equal(0, await QuantityAsync(milk));
        Assert.Empty(await MovementsAsync(milk)); // a quantity of zero is explained by no movements
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task AnOrdersMovements_NameTheOrder_AndTheActorWhoseOrderItWas()
    {
        var (store, milk) = await AProductAsync();
        var actor = await StockActors.IdAsync();
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(10));
        var orderId = Guid.CreateVersion7();
        var order = Order.Place(orderId, store, actor, "1 High Street", DateTimeOffset.UtcNow, [new OrderLine(Guid.CreateVersion7(), orderId, milk, "Milk", 2.5m, 4)]);
        await using (var db = await NewContextAsync())
        {
            await new OrderRepository(db).AddAsync(order, CancellationToken.None);
        }

        await ChangeAsync(store, milk, new StockChange(-4, StockMovementReason.OrderPlaced, actor, orderId));
        await ChangeAsync(store, milk, new StockChange(4, StockMovementReason.OrderCancelled, actor, orderId));

        Assert.Equal(
            [("Received", (Guid?)null), ("OrderPlaced", orderId), ("OrderCancelled", orderId)],
            (await MovementsAsync(milk)).Select(row => (row.Reason, row.OrderId)));
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task AChangeThatMakesNoSense_IsRefusedBeforeAnySql_AndChangesNothing()
    {
        var (store, milk) = await AProductAsync();
        var actor = await StockActors.IdAsync();

        // The opening balance is the migration's alone; a delivery cannot remove; an order's movement needs an order.
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(store, milk, new StockChange(5, StockMovementReason.OpeningBalance, actor)));
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(store, milk, new StockChange(-5, StockMovementReason.Received, actor)));
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(store, milk, new StockChange(-5, StockMovementReason.OrderPlaced, actor)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChangeAsync(store, milk, new StockChange(0, StockMovementReason.Correction, actor)));

        Assert.Null(await QuantityAsync(milk));
        Assert.Empty(await MovementsAsync(milk));
    }

    [Fact]
    public async Task AnActorWhoDoesNotExist_IsAnException_AndTheQuantityDoesNotMove()
    {
        var (store, milk) = await AProductAsync();
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(5));

        // The quantity and the movement are one statement, so when the movement is refused the quantity is not changed either.
        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => ChangeAsync(store, milk, new StockChange(3, StockMovementReason.Received, Guid.CreateVersion7())));

        Assert.Equal("fk_stock_movements_users_actor_id", exception.ConstraintName);
        Assert.Equal(5, await QuantityAsync(milk));
        Assert.Equal([5], (await MovementsAsync(milk)).Select(row => row.Delta));
    }

    [Fact]
    public async Task ManyChangesAtTheSameTime_AllLeaveTheirMovements_AndTheLedgerAddsUp()
    {
        var (store, milk) = await AProductAsync();
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(100));

        // Thirty additions of 2 and thirty removals of 1, together: the removals can never fail (there is always enough), so all sixty count.
        var work = Enumerable.Range(0, 30).Select(async _ => await ChangeAsync(store, milk, await StockActors.ChangeAsync(2)))
            .Concat(Enumerable.Range(0, 30).Select(async _ => await ChangeAsync(store, milk, await StockActors.ChangeAsync(-1))));
        var results = await Task.WhenAll(work);

        Assert.All(results, quantity => Assert.NotNull(quantity));
        Assert.Equal(130, await QuantityAsync(milk)); // 100 + 60 - 30
        Assert.Equal(61, (await MovementsAsync(milk)).Count);
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task TwentyRemovalsFromStockOfFive_FiveSucceed_FiveMovementsExist_AndTheLedgerAddsUpToZero()
    {
        var (store, milk) = await AProductAsync();
        await ChangeAsync(store, milk, await StockActors.ChangeAsync(5));

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => await ChangeAsync(store, milk, await StockActors.ChangeAsync(-1))));

        Assert.Equal(5, results.Count(quantity => quantity is not null));
        Assert.Equal(0, await QuantityAsync(milk));
        Assert.Equal(6, (await MovementsAsync(milk)).Count); // the delivery, and one movement for each of the five that went through
        await Ledger.AgreesAsync(store);
    }

    [Fact]
    public async Task TheHistory_IsNewestFirst_PagedWithTheTotal_AndNeverAnotherStores()
    {
        var (store, milk) = await AProductAsync();
        var (other, otherMilk) = await AProductAsync();
        var clock = new AdjustableTimeProvider();
        var actor = await StockActors.IdAsync();
        foreach (var delta in new[] { 10, -1, 5, -2, 3 })
        {
            await ChangeAsync(store, milk, new StockChange(delta, delta > 0 ? StockMovementReason.Received : StockMovementReason.Correction, actor), clock);
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        await ChangeAsync(other, otherMilk, await StockActors.ChangeAsync(99));
        await using var db = await NewContextAsync();
        var repository = new InventoryItemRepository(db);

        var first = await repository.ListMovementsAsync(store, milk, 1, 2, CancellationToken.None);
        var last = await repository.ListMovementsAsync(store, milk, 3, 2, CancellationToken.None);
        var notMine = await repository.ListMovementsAsync(other, milk, 1, 20, CancellationToken.None); // my product through another store
        var past = await repository.ListMovementsAsync(store, milk, 9, 2, CancellationToken.None);

        Assert.Equal([3, -2], first.Items.Select(movement => movement.Delta)); // newest first
        Assert.Equal([10], last.Items.Select(movement => movement.Delta));
        Assert.Equal((5, 5), (first.TotalCount, last.TotalCount));
        Assert.Empty(notMine.Items);
        Assert.Equal(0, notMine.TotalCount);
        Assert.Empty(past.Items);
    }

    [Fact]
    public async Task MovementsMadeInTheSameInstant_KeepAStableOrder_BecauseTheIdBreaksTies()
    {
        var (store, milk) = await AProductAsync();
        var clock = new AdjustableTimeProvider(); // never advanced: every movement has the same time
        var actor = await StockActors.IdAsync();
        for (var i = 0; i < 4; i++)
        {
            await ChangeAsync(store, milk, new StockChange(i + 1, StockMovementReason.Received, actor), clock);
        }

        await using var db = await NewContextAsync();
        var repository = new InventoryItemRepository(db);
        var first = await repository.ListMovementsAsync(store, milk, 1, 2, CancellationToken.None);
        var second = await repository.ListMovementsAsync(store, milk, 2, 2, CancellationToken.None);

        Assert.Equal([4, 3, 2, 1], [.. first.Items.Select(movement => movement.Delta), .. second.Items.Select(movement => movement.Delta)]); // newest first, by id
    }
}
