using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The membership repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is.
/// The unique index on the user is the only thing that can say "already in a store" under concurrency, so the race is run for real.
/// </summary>
public class StoreMemberRepositoryTests
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

    private static async Task<Guid> StoredUserAsync()
    {
        var user = new User(Guid.CreateVersion7(), "Asha", "Rai", $"asha-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        await using var db = await NewContextAsync();
        Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
        return user.Id;
    }

    private static async Task<bool> AddAsync(Guid storeId, Guid userId)
    {
        await using var db = await NewContextAsync();
        return await new StoreMemberRepository(db).TryAddAsync(new StoreMember(Guid.CreateVersion7(), storeId, userId), CancellationToken.None);
    }

    private static async Task<long> MembershipsOfAsync(Guid userId) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from store_members where user_id = '{userId}'");

    [Fact]
    public async Task AMembership_IsRecorded()
    {
        var userId = await StoredUserAsync();

        Assert.True(await AddAsync(await StoredStoreAsync(), userId));

        Assert.Equal(1L, await MembershipsOfAsync(userId));
    }

    [Fact]
    public async Task AUserWhoRunsAStoreAlready_IsRefused_ForTheSameStoreAndForAnother()
    {
        var store = await StoredStoreAsync();
        var userId = await StoredUserAsync();
        Assert.True(await AddAsync(store, userId));

        Assert.False(await AddAsync(store, userId));
        Assert.False(await AddAsync(await StoredStoreAsync(), userId));
        Assert.Equal(1L, await MembershipsOfAsync(userId));
    }

    [Fact]
    public async Task TheContext_IsUsableAfterARefusedAdd()
    {
        var store = await StoredStoreAsync();
        var userId = await StoredUserAsync();
        Assert.True(await AddAsync(store, userId));
        await using var db = await NewContextAsync();
        var repository = new StoreMemberRepository(db);
        Assert.False(await repository.TryAddAsync(new StoreMember(Guid.CreateVersion7(), store, userId), CancellationToken.None));

        Assert.True(await repository.TryAddAsync(new StoreMember(Guid.CreateVersion7(), store, await StoredUserAsync()), CancellationToken.None));
    }

    [Fact]
    public async Task AStoreCanHaveManyMembers()
    {
        var store = await StoredStoreAsync();

        Assert.True(await AddAsync(store, await StoredUserAsync()));
        Assert.True(await AddAsync(store, await StoredUserAsync()));
    }

    [Fact]
    public async Task ManyRequestsAssigningTheSameUserAtTheSameTime_OneWins()
    {
        var userId = await StoredUserAsync();
        var stores = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => StoredStoreAsync()));

        var results = await Task.WhenAll(stores.Select(store => AddAsync(store, userId)));

        Assert.Equal(1, results.Count(added => added));
        Assert.Equal(1L, await MembershipsOfAsync(userId));
    }

    [Fact]
    public async Task AStoreOrAUserThatDoesNotExist_IsAnException_NotAnOrdinaryRefusal()
    {
        // The foreign keys refuse the row. That is a bug in the caller, so it must not come back as false ("already in a store").
        await Assert.ThrowsAsync<DbUpdateException>(async () => await AddAsync(Guid.CreateVersion7(), await StoredUserAsync()));
        await Assert.ThrowsAsync<DbUpdateException>(async () => await AddAsync(await StoredStoreAsync(), Guid.CreateVersion7()));
    }
}
