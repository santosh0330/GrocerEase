using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The stock repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is. The point
/// of the design is what happens when many requests change one quantity at once, so those races are run for real: only the database can
/// show that the quantity is never driven below zero by requests that each saw enough.
/// </summary>
public class InventoryItemRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static async Task<(Guid StoreId, Guid ProductId)> AProductAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        var product = new Product(Guid.CreateVersion7(), store.Id, "Milk", "", 2.5m);
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        Assert.True(await new ProductRepository(db).TryAddAsync(product, CancellationToken.None));
        return (store.Id, product.Id);
    }

    private static async Task<int?> AdjustAsync(Guid storeId, Guid productId, int delta)
    {
        await using var db = await NewContextAsync();
        return await new InventoryItemRepository(db).TryAdjustAsync(storeId, productId, delta, CancellationToken.None);
    }

    private static async Task<int?> QuantityAsync(Guid productId) =>
        await PostgresServer.ScalarAsync<int?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select quantity_on_hand from inventory_items where product_id = '{productId}'");

    [Fact]
    public async Task AddingStock_ToAProductWithNoRow_MakesTheRow()
    {
        var (store, product) = await AProductAsync();
        Assert.Null(await QuantityAsync(product)); // no row: none on hand

        var quantity = await AdjustAsync(store, product, 12);

        Assert.Equal(12, quantity);
        Assert.Equal(12, await QuantityAsync(product));
    }

    [Fact]
    public async Task Changes_AreAmounts_SoTheyAddUp_InEitherDirection()
    {
        var (store, product) = await AProductAsync();

        Assert.Equal(10, await AdjustAsync(store, product, 10));
        Assert.Equal(15, await AdjustAsync(store, product, 5));
        Assert.Equal(12, await AdjustAsync(store, product, -3));
        Assert.Equal(12, await QuantityAsync(product));
    }

    [Fact]
    public async Task RemovingMoreThanThereIs_IsRefused_AndChangesNothing()
    {
        var (store, product) = await AProductAsync();
        await AdjustAsync(store, product, 5);

        Assert.Null(await AdjustAsync(store, product, -6));

        Assert.Equal(5, await QuantityAsync(product));
    }

    [Fact]
    public async Task RemovingExactlyWhatThereIs_LeavesZero_ButTheRowStays()
    {
        var (store, product) = await AProductAsync();
        await AdjustAsync(store, product, 5);

        Assert.Equal(0, await AdjustAsync(store, product, -5));
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task RemovingFromAProductWithNoRow_IsRefused_AndMakesNoRow()
    {
        var (store, product) = await AProductAsync();

        Assert.Null(await AdjustAsync(store, product, -1));

        Assert.Null(await QuantityAsync(product));
    }

    [Fact]
    public async Task AddingPastTheMaximum_IsRefused_AndChangesNothing()
    {
        var (store, product) = await AProductAsync();
        Assert.Equal(InventoryItem.MaxQuantity, await AdjustAsync(store, product, InventoryItem.MaxQuantity));

        Assert.Null(await AdjustAsync(store, product, 1));

        Assert.Equal(InventoryItem.MaxQuantity, await QuantityAsync(product));
        Assert.Equal(0, await AdjustAsync(store, product, -InventoryItem.MaxQuantity)); // and the largest change in the other direction fits too
    }

    [Fact]
    public async Task AChangeThroughAnotherStore_ChangesNothing_WhenTheRowExists()
    {
        var (store, product) = await AProductAsync();
        await AdjustAsync(store, product, 5);
        var (otherStore, _) = await AProductAsync();

        Assert.Null(await AdjustAsync(otherStore, product, -1)); // the statement names the store: another store's request finds nothing
        Assert.Null(await AdjustAsync(otherStore, product, 3)); // and cannot add either: the row is not its

        Assert.Equal(5, await QuantityAsync(product));
    }

    [Fact]
    public async Task AProductThatIsNotInTheStoreNamed_IsAnException_ForTheFirstAdd()
    {
        var (_, product) = await AProductAsync();
        var (otherStore, _) = await AProductAsync();

        // No row yet: adding would insert (product, otherStore), which the composite foreign key refuses. That is a bug in the caller (the
        // handler checks the product is in the store first), so it must be an exception and not an ordinary "refused".
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => AdjustAsync(otherStore, product, 3));
    }

    [Fact]
    public async Task EightRequestsAddingOneEach_AtTheSameTime_AllCount()
    {
        var (store, product) = await AProductAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AdjustAsync(store, product, 1)));

        Assert.All(results, quantity => Assert.NotNull(quantity));
        Assert.Equal(8, await QuantityAsync(product)); // a read followed by a write would lose some of them
        Assert.Equal(Enumerable.Range(1, 8), results.Select(quantity => quantity!.Value).Order()); // each saw a different, consecutive total
    }

    [Fact]
    public async Task TwentyRequestsRemovingOneEach_FromStockOfFive_ExactlyFiveSucceed_AndTheQuantityNeverGoesBelowZero()
    {
        var (store, product) = await AProductAsync();
        await AdjustAsync(store, product, 5);

        // Each of these could have read "5 left". Only the statement's own condition can stop the sixth.
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AdjustAsync(store, product, -1)));

        Assert.Equal(5, results.Count(quantity => quantity is not null));
        Assert.Equal(15, results.Count(quantity => quantity is null));
        Assert.Equal(0, await QuantityAsync(product));
    }

    [Fact]
    public async Task AMixOfAddsAndRemoves_AtTheSameTime_EndsAtExactlyTheirSum()
    {
        var (store, product) = await AProductAsync();
        await AdjustAsync(store, product, 100);

        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => AdjustAsync(store, product, i % 2 == 0 ? 3 : -2)));

        // 15 adds of 3 and 15 removes of 2, from 100: never near zero, so every one succeeds and the total is exact.
        Assert.All(results, quantity => Assert.NotNull(quantity));
        Assert.Equal(100 + (15 * 3) - (15 * 2), await QuantityAsync(product));
    }
}
