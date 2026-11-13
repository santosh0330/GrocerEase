using GrocerEase.Application.Products.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The product repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is. The
/// partial unique index is the only thing that can say "taken" under concurrency, so the race is run for real.
/// </summary>
public class ProductRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static async Task<Guid> StoredStoreAsync()
    {
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        await using var db = await NewContextAsync();
        Assert.True(await new StoreRepository(db).TryAddAsync(store, CancellationToken.None));
        return store.Id;
    }

    private static Product NewProduct(Guid storeId, string? name = null, decimal price = 2.50m) =>
        new(Guid.CreateVersion7(), storeId, name ?? $"Milk {Guid.NewGuid():N}", "Fresh milk", price);

    private static async Task<bool> AddAsync(Product product)
    {
        await using var db = await NewContextAsync();
        return await new ProductRepository(db).TryAddAsync(product, CancellationToken.None);
    }

    private static async Task<Product?> GetAsync(Guid storeId, Guid productId)
    {
        await using var db = await NewContextAsync();
        return await new ProductRepository(db).GetLiveAsync(storeId, productId, CancellationToken.None);
    }

    [Fact]
    public async Task AStoredProduct_IsFoundInItsStore_WithAllItsValues_AndThePriceKeepsItsCents()
    {
        var store = await StoredStoreAsync();
        var product = new Product(Guid.CreateVersion7(), store, "Milk", "Fresh milk", 1.10m);
        Assert.True(await AddAsync(product));

        var found = await GetAsync(store, product.Id);

        Assert.NotNull(found);
        Assert.Equal((product.Id, store, "Milk", "Fresh milk", 1.10m, null), (found.Id, found.StoreId, found.Name, found.Description, found.Price, found.ArchivedAt));
    }

    private static async Task<GrocerEase.Application.Common.PagedResult<Product>> ListAsync(Guid storeId, int page, int pageSize)
    {
        await using var db = await NewContextAsync();
        return await new ProductRepository(db).ListLiveAsync(storeId, page, pageSize, CancellationToken.None);
    }

    [Fact]
    public async Task TheList_IsPaged_OrderedByName_AndSaysHowLongTheWholeListIs()
    {
        var store = await StoredStoreAsync();
        foreach (var name in new[] { "Eggs", "Bread", "Milk", "Apples", "Cheese" })
        {
            Assert.True(await AddAsync(NewProduct(store, name)));
        }

        var first = await ListAsync(store, 1, 2);
        var last = await ListAsync(store, 3, 2);

        Assert.Equal(["Apples", "Bread"], first.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal(["Milk"], last.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal((5, 5, 1), (first.TotalCount, last.TotalCount, last.Items.Count));
    }

    [Fact]
    public async Task TheList_LeavesOutArchivedProducts_AndAnotherStoresProducts()
    {
        var store = await StoredStoreAsync();
        Assert.True(await AddAsync(NewProduct(store, "Milk")));
        Assert.True(await AddAsync(new Product(Guid.CreateVersion7(), store, "Old", "", 1m, DateTimeOffset.UtcNow)));
        Assert.True(await AddAsync(NewProduct(await StoredStoreAsync(), "Elsewhere")));

        var page = await ListAsync(store, 1, 20);

        Assert.Equal(["Milk"], page.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task APagePastTheEnd_IsEmpty_AndTheTotalIsStillRight()
    {
        var store = await StoredStoreAsync();
        Assert.True(await AddAsync(NewProduct(store)));

        var page = await ListAsync(store, 50, 20);

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    private static async Task<ProductUpdateOutcome> UpdateAsync(Product product)
    {
        await using var db = await NewContextAsync();
        return await new ProductRepository(db).TryUpdateAsync(product, CancellationToken.None);
    }

    [Fact]
    public async Task TheDetails_AreWritten_AndTheIdAndTheStoreStay()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        var outcome = await UpdateAsync(milk.WithDetails("Oat milk", "Vegan", 3.20m));

        Assert.Equal(ProductUpdateOutcome.Updated, outcome);
        var found = await GetAsync(store, milk.Id);
        Assert.Equal((milk.Id, store, "Oat milk", "Vegan", 3.20m), (found?.Id, found?.StoreId, found?.Name, found?.Description, found?.Price));
    }

    private static async Task<int?> VersionInDatabaseAsync(Guid productId) =>
        await PostgresServer.ScalarAsync<int?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select version from products where id = '{productId}'");

    [Fact]
    public async Task ANewProduct_IsStoredAsVersionOne_AndReadBackWithIt()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        Assert.Equal(1, await VersionInDatabaseAsync(milk.Id));
        Assert.Equal(1, (await GetAsync(store, milk.Id))!.Version);
    }

    [Fact]
    public async Task EveryEdit_IsTheNextVersion_AndTheListShowsIt()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        var second = milk.WithDetails("Oat milk", "Vegan", 3.20m);
        Assert.Equal(ProductUpdateOutcome.Updated, await UpdateAsync(second));
        Assert.Equal(ProductUpdateOutcome.Updated, await UpdateAsync(second.WithDetails("Oat milk", "Vegan", 3.40m)));

        Assert.Equal(3, await VersionInDatabaseAsync(milk.Id));
        Assert.Equal(3, (await GetAsync(store, milk.Id))!.Version);
        Assert.Equal([3], (await ListAsync(store, 1, 20)).Items.Select(product => product.Version));
    }

    [Fact]
    public async Task AFailedEdit_DoesNotChangeTheVersion()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store, "Milk");
        var bread = NewProduct(store, "Bread");
        Assert.True(await AddAsync(milk));
        Assert.True(await AddAsync(bread));

        Assert.Equal(ProductUpdateOutcome.DuplicateName, await UpdateAsync(milk.WithDetails("Bread", "", 1m))); // the name is taken
        Assert.Equal(ProductUpdateOutcome.NotFound, await UpdateAsync(NewProduct(store, "Nobody")));

        Assert.Equal(1, await VersionInDatabaseAsync(milk.Id));
    }

    [Fact]
    public async Task Archiving_IsAChangeToo_SoItIsTheNextVersion()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));
        await using var db = await NewContextAsync();

        Assert.True(await new ProductRepository(db).TryArchiveAsync(store, milk.Id, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Equal(2, await VersionInDatabaseAsync(milk.Id));
    }

    [Fact]
    public async Task AProduct_MayBeGivenItsOwnNameAgain()
    {
        var milk = NewProduct(await StoredStoreAsync());
        Assert.True(await AddAsync(milk));

        Assert.Equal(ProductUpdateOutcome.Updated, await UpdateAsync(milk.WithDetails(milk.Name, "New text", 9m)));
    }

    [Fact]
    public async Task TheNameOfAnotherLiveProduct_IsADuplicate_AndNothingChanges()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        var bread = NewProduct(store);
        Assert.True(await AddAsync(milk));
        Assert.True(await AddAsync(bread));

        var outcome = await UpdateAsync(milk.WithDetails(bread.Name, "x", 1m));

        Assert.Equal(ProductUpdateOutcome.DuplicateName, outcome);
        Assert.Equal(milk.Name, (await GetAsync(store, milk.Id))?.Name);
    }

    [Fact]
    public async Task AnArchivedProductsName_MayBeTaken_ByRenamingALiveOne()
    {
        var store = await StoredStoreAsync();
        var old = new Product(Guid.CreateVersion7(), store, "Taken once", "", 1m, DateTimeOffset.UtcNow);
        var live = NewProduct(store);
        Assert.True(await AddAsync(old));
        Assert.True(await AddAsync(live));

        Assert.Equal(ProductUpdateOutcome.Updated, await UpdateAsync(live.WithDetails("Taken once", "", 1m)));
    }

    [Fact]
    public async Task AProductOfAnotherStore_AnArchivedOne_AndAnUnknownOne_AreNotFound_AndNothingIsTouched()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        var archived = new Product(Guid.CreateVersion7(), store, "Old", "", 1m, DateTimeOffset.UtcNow);
        Assert.True(await AddAsync(milk));
        Assert.True(await AddAsync(archived));
        var throughAnotherStore = new Product(milk.Id, await StoredStoreAsync(), "Hacked", "", 1m); // the id of milk, claiming another store

        Assert.Equal(ProductUpdateOutcome.NotFound, await UpdateAsync(throughAnotherStore));
        Assert.Equal(ProductUpdateOutcome.NotFound, await UpdateAsync(archived.WithDetails("Back", "", 1m)));
        Assert.Equal(ProductUpdateOutcome.NotFound, await UpdateAsync(NewProduct(store)));

        Assert.Equal(milk.Name, (await GetAsync(store, milk.Id))?.Name);
        Assert.Null(await GetAsync(store, archived.Id)); // an edit does not bring an archived product back
    }

    [Fact]
    public async Task ManyProductsRenamedToTheSameNameAtTheSameTime_OneWins()
    {
        var store = await StoredStoreAsync();
        var products = new List<Product>();
        for (var i = 0; i < 8; i++)
        {
            var product = NewProduct(store);
            Assert.True(await AddAsync(product));
            products.Add(product);
        }

        var name = $"Race {Guid.NewGuid():N}";
        var outcomes = await Task.WhenAll(products.Select(product => UpdateAsync(product.WithDetails(name, "", 1m))));

        Assert.Equal(1, outcomes.Count(outcome => outcome == ProductUpdateOutcome.Updated));
        Assert.Equal(7, outcomes.Count(outcome => outcome == ProductUpdateOutcome.DuplicateName));
    }

    private static async Task<bool> ArchiveAsync(Guid storeId, Guid productId)
    {
        await using var db = await NewContextAsync();
        return await new ProductRepository(db).TryArchiveAsync(storeId, productId, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    [Fact]
    public async Task AnArchivedProduct_LeavesTheCatalogue_FreesItsName_AndTheRowStays()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        Assert.True(await ArchiveAsync(store, milk.Id));

        Assert.Null(await GetAsync(store, milk.Id));
        Assert.Empty((await ListAsync(store, 1, 20)).Items);
        Assert.True(await AddAsync(NewProduct(store, milk.Name))); // the name is free again
        Assert.Equal(
            2L,
            await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from products where store_id = '{store}' and name = '{milk.Name}'"));
        Assert.True(await PostgresServer.ScalarAsync<bool>(
            await PostgresServer.ApplicationConnectionStringAsync(), $"select archived_at is not null from products where id = '{milk.Id}'"));
    }

    [Fact]
    public async Task ArchivingTwice_ThroughAnotherStore_OrAnUnknownProduct_ChangesNothing_AndSaysSo()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        Assert.False(await ArchiveAsync(await StoredStoreAsync(), milk.Id)); // another store's: untouched
        Assert.False(await ArchiveAsync(store, Guid.CreateVersion7()));
        Assert.NotNull(await GetAsync(store, milk.Id));
        Assert.True(await ArchiveAsync(store, milk.Id));
        Assert.False(await ArchiveAsync(store, milk.Id)); // already archived
    }

    [Fact]
    public async Task ManyRequestsArchivingTheSameProductAtTheSameTime_OneWins()
    {
        var store = await StoredStoreAsync();
        var milk = NewProduct(store);
        Assert.True(await AddAsync(milk));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ArchiveAsync(store, milk.Id)));

        Assert.Equal(1, results.Count(archived => archived));
    }

    [Fact]
    public async Task AProduct_IsNotFoundThroughAnotherStore()
    {
        var product = NewProduct(await StoredStoreAsync());
        Assert.True(await AddAsync(product));

        Assert.Null(await GetAsync(await StoredStoreAsync(), product.Id));
    }

    [Fact]
    public async Task AnArchivedProduct_IsNotFound()
    {
        var store = await StoredStoreAsync();
        var product = new Product(Guid.CreateVersion7(), store, "Milk", "", 1m, DateTimeOffset.UtcNow);
        Assert.True(await AddAsync(product));

        Assert.Null(await GetAsync(store, product.Id));
    }

    [Fact]
    public async Task ASecondLiveProductWithTheSameName_IsRefused_ButAnotherStoreMayUseIt()
    {
        var store = await StoredStoreAsync();
        var first = NewProduct(store);
        Assert.True(await AddAsync(first));

        Assert.False(await AddAsync(NewProduct(store, first.Name)));
        Assert.True(await AddAsync(NewProduct(await StoredStoreAsync(), first.Name)));
    }

    [Fact]
    public async Task TheContext_IsUsableAfterARefusedAdd()
    {
        var store = await StoredStoreAsync();
        var first = NewProduct(store);
        Assert.True(await AddAsync(first));
        await using var db = await NewContextAsync();
        var repository = new ProductRepository(db);
        Assert.False(await repository.TryAddAsync(NewProduct(store, first.Name), CancellationToken.None));

        Assert.True(await repository.TryAddAsync(NewProduct(store), CancellationToken.None));
    }

    [Fact]
    public async Task EightProductsWithTheSameNameAddedAtTheSameTime_OneWins()
    {
        var store = await StoredStoreAsync();
        var name = $"Race {Guid.NewGuid():N}";

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AddAsync(NewProduct(store, name))));

        Assert.Equal(1, results.Count(added => added));
    }

    [Fact]
    public async Task AProductOfAStoreThatDoesNotExist_IsAnException_NotAnOrdinaryRefusal() =>
        await Assert.ThrowsAsync<DbUpdateException>(async () => await AddAsync(NewProduct(Guid.CreateVersion7())));
}
