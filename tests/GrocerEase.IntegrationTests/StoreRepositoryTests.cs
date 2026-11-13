using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The store repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is. The
/// unique name index is the only thing that can say "taken" under concurrency, so the race is run for real.
/// </summary>
public class StoreRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static Store NewStore(string? name = null) => new(Guid.CreateVersion7(), name ?? $"Store {Guid.NewGuid():N}", "1 High Street");

    private static async Task<bool> AddAsync(Store store)
    {
        await using var db = await NewContextAsync();
        return await new StoreRepository(db).TryAddAsync(store, CancellationToken.None);
    }

    [Fact]
    public async Task AStoredStore_IsFoundById_WithAllItsValues()
    {
        var store = NewStore();
        Assert.True(await AddAsync(store));

        await using var db = await NewContextAsync();
        var found = await new StoreRepository(db).GetByIdAsync(store.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal((store.Id, store.Name, store.Address), (found.Id, found.Name, found.Address));
    }

    [Fact]
    public async Task AnUnknownId_IsNotFound()
    {
        await using var db = await NewContextAsync();

        Assert.Null(await new StoreRepository(db).GetByIdAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task ASecondStoreWithTheSameName_IsRefused_AndTheFirstIsUntouched()
    {
        var first = NewStore();
        Assert.True(await AddAsync(first));

        await using var db = await NewContextAsync();
        var repository = new StoreRepository(db);
        var added = await repository.TryAddAsync(new Store(Guid.CreateVersion7(), first.Name, "2 Other Road"), CancellationToken.None);

        Assert.False(added);
        Assert.Equal("1 High Street", (await repository.GetByIdAsync(first.Id, CancellationToken.None))?.Address);
    }

    [Fact]
    public async Task TheContext_IsUsableAfterARefusedAdd()
    {
        var first = NewStore();
        Assert.True(await AddAsync(first));
        await using var db = await NewContextAsync();
        var repository = new StoreRepository(db);
        Assert.False(await repository.TryAddAsync(new Store(Guid.CreateVersion7(), first.Name, "x"), CancellationToken.None));

        Assert.True(await repository.TryAddAsync(NewStore(), CancellationToken.None)); // would try to insert the refused one again if it were still tracked
    }

    [Fact]
    public async Task TwoStoresWithTheSameNameAddedAtTheSameTime_OneWins_AndTheOtherIsToldSo()
    {
        var name = $"Race {Guid.NewGuid():N}";

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AddAsync(new Store(Guid.CreateVersion7(), name, "x"))));

        Assert.Equal(1, results.Count(added => added));
        Assert.Equal(7, results.Count(added => !added));
    }

    [Fact]
    public async Task TheList_IsOrderedByName()
    {
        var prefix = $"List {Guid.NewGuid():N} ";
        Assert.True(await AddAsync(NewStore(prefix + "b")));
        Assert.True(await AddAsync(NewStore(prefix + "a")));

        // The database holds every store the other tests made, so walk all the pages, as a client would.
        await using var db = await NewContextAsync();
        var repository = new StoreRepository(db);
        var listed = new List<Store>();
        for (var page = 1; ; page++)
        {
            var next = await repository.ListAsync(page, 100, CancellationToken.None);
            listed.AddRange(next.Items);
            if (next.Items.Count == 0 || listed.Count >= next.TotalCount)
            {
                break;
            }
        }

        Assert.Equal([prefix + "a", prefix + "b"], listed.Where(store => store.Name.StartsWith(prefix, StringComparison.Ordinal)).Select(store => store.Name), StringComparer.Ordinal);
        Assert.Equal(listed.OrderBy(store => store.Name, StringComparer.Ordinal).Select(store => store.Id), listed.Select(store => store.Id));
        Assert.Equal(listed.Count, listed.Select(store => store.Id).Distinct().Count()); // no store repeated across pages
    }

    [Fact]
    public async Task APage_HasAtMostPageSizeItems_TheTotalSaysHowManyThereAre_AndAPagePastTheEndIsEmpty()
    {
        Assert.True(await AddAsync(NewStore()));
        await using var db = await NewContextAsync();
        var repository = new StoreRepository(db);

        var first = await repository.ListAsync(1, 1, CancellationToken.None);
        var past = await repository.ListAsync(1_000_000, 100, CancellationToken.None);

        Assert.Single(first.Items);
        Assert.True(first.TotalCount >= 1);
        Assert.Empty(past.Items);
        Assert.True(past.TotalCount >= first.TotalCount); // other tests add stores at the same time: the total can only have grown
    }
}
