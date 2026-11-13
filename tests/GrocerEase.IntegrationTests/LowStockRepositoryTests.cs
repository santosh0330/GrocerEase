using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The low-stock listing against a real PostgreSQL, connected as the least-privilege application role. The point of it is the join: "at or
/// below the level" in SQL, the live products only, and the store on both sides.
/// </summary>
public class LowStockRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static async Task<Guid> AStoreAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        return store.Id;
    }

    /// <summary>A product with the stock and level given (null level: none; null stock: no row at all).</summary>
    private static async Task<Guid> AProductAsync(Guid storeId, string name, int? stock, int? level)
    {
        var product = new Product(Guid.CreateVersion7(), storeId, name, "", 1m);
        await using var db = await NewContextAsync();
        Assert.True(await new ProductRepository(db).TryAddAsync(product, CancellationToken.None));
        var inventory = new InventoryItemRepository(db);
        if (stock is { } quantity && quantity > 0)
        {
            Assert.Equal(quantity, await inventory.TryAdjustAsync(storeId, product.Id, quantity, CancellationToken.None));
        }

        if (level is { } threshold || stock is not null)
        {
            await inventory.SetLowStockThresholdAsync(storeId, product.Id, level, CancellationToken.None); // makes the row at zero if there is none
        }

        return product.Id;
    }

    private static async Task<PagedResult<InventoryEntryResult>> ListAsync(Guid storeId, int page = 1, int pageSize = 20)
    {
        await using var db = await NewContextAsync();
        return await new LowStockRepository(db).ListLowAsync(storeId, page, pageSize, CancellationToken.None);
    }

    [Fact]
    public async Task AtTheLevelIsLow_OneAboveIsNot_AndTheBoundaryIsTheSameAsTheEntitys()
    {
        var store = await AStoreAsync();
        var below = await AProductAsync(store, "Below", 4, 5);
        var at = await AProductAsync(store, "At", 5, 5);
        await AProductAsync(store, "Above", 6, 5);

        var page = await ListAsync(store);

        Assert.Equal([at, below], page.Items.Select(entry => entry.ProductId)); // by name: "At" before "Below"
        Assert.All(page.Items, entry => Assert.True(entry.IsLow));
        Assert.Equal(2, page.TotalCount);
        // The SQL and the entity agree on every quantity around the boundary: the entity is the definition, the query a copy of it.
        foreach (var quantity in new[] { 4, 5, 6 })
        {
            Assert.Equal(quantity <= 5, InventoryItem.IsLowAt(quantity, 5));
        }
    }

    [Fact]
    public async Task ALevelOfZero_IsLowOnlyWhenNothingIsLeft()
    {
        var store = await AStoreAsync();
        var gone = await AProductAsync(store, "Gone", 0, 0);
        await AProductAsync(store, "OneLeft", 1, 0);

        var page = await ListAsync(store);

        Assert.Equal([gone], page.Items.Select(entry => entry.ProductId));
    }

    [Fact]
    public async Task AProductWithNoLevel_NeverAppears_WhateverItsStock_AndNeitherDoesOneWithNoRow()
    {
        var store = await AStoreAsync();
        await AProductAsync(store, "NoLevelNoStock", 0, null);
        await AProductAsync(store, "NoLevelSomeStock", 3, null);
        await AProductAsync(store, "NoRowAtAll", null, null);

        var page = await ListAsync(store);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task AnArchivedProduct_NeverAppears_AndTheTotalDoesNotCountIt()
    {
        var store = await AStoreAsync();
        var live = await AProductAsync(store, "Live", 1, 5);
        var archived = await AProductAsync(store, "Archived", 1, 5);
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update products set archived_at = now() where id = '{archived}'");

        var page = await ListAsync(store);

        Assert.Equal([live], page.Items.Select(entry => entry.ProductId));
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task AnotherStoresProducts_NeverAppear_AndEachStoreSeesItsOwn()
    {
        var mine = await AStoreAsync();
        var theirs = await AStoreAsync();
        var myMilk = await AProductAsync(mine, "Milk", 1, 5);
        var theirMilk = await AProductAsync(theirs, "Milk", 1, 5);

        Assert.Equal([myMilk], (await ListAsync(mine)).Items.Select(entry => entry.ProductId));
        Assert.Equal([theirMilk], (await ListAsync(theirs)).Items.Select(entry => entry.ProductId));
    }

    [Fact]
    public async Task TheEntriesCarryTheQuantityAndTheLevel_AndAreOrderedByName_WithAStablePage()
    {
        var store = await AStoreAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "E", "C", "A", "D", "B" })
        {
            ids.Add(await AProductAsync(store, name, 2, 3));
        }

        var second = await ListAsync(store, 2, 2);
        var past = await ListAsync(store, 9, 2);

        Assert.Equal(["C", "D"], second.Items.Select(entry => entry.ProductName), StringComparer.Ordinal);
        Assert.All(second.Items, entry => Assert.Equal((2, 3), (entry.QuantityOnHand, entry.LowStockThreshold)));
        Assert.Equal(5, second.TotalCount);
        Assert.Empty(past.Items);
        Assert.Equal(5, past.TotalCount);
    }
}
