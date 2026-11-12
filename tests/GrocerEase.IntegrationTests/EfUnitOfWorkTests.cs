using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The unit of work against a real PostgreSQL, connected as the least-privilege application role, as the API is. Only the real
/// database can show that writes made through different repositories, and through different kinds of write, are kept or undone
/// together. What a row looks like afterwards is read on a separate connection, so a test sees what was committed, not what its
/// own transaction can see.
/// </summary>
public class EfUnitOfWorkTests
{
    private static readonly TimeSpan ThreeDays = TimeSpan.FromDays(3);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static User NewUser() =>
        new(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);

    private static RefreshToken NewToken(Guid userId) =>
        RefreshToken.Start(
            userId, System.Security.Cryptography.RandomNumberGenerator.GetBytes(RefreshToken.HashLength), DateTimeOffset.UtcNow, ThreeDays, ThirtyDays);

    private static async Task<long> CountAsync(string sql) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), sql);

    private static async Task<long> UsersWithIdAsync(Guid id) => await CountAsync($"select count(*) from users where id = '{id}'");

    private static async Task<long> TokensOfAsync(Guid userId) => await CountAsync($"select count(*) from refresh_tokens where user_id = '{userId}'");

    [Fact]
    public async Task WhatIsWrittenThroughTwoRepositories_IsKeptTogether_WhenTheUnitIsCommitted()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        var unitOfWork = new EfUnitOfWork(db);

        await using (var unit = await unitOfWork.BeginAsync(CancellationToken.None))
        {
            Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
            await new RefreshTokenRepository(db, NullLogger<RefreshTokenRepository>.Instance).AddAsync(NewToken(user.Id), CancellationToken.None);
            await unit.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(1L, await UsersWithIdAsync(user.Id));
        Assert.Equal(1L, await TokensOfAsync(user.Id));
    }

    [Fact]
    public async Task WhatIsWrittenThroughTwoRepositories_IsUndoneTogether_WhenTheUnitIsDisposedWithoutACommit()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        var unitOfWork = new EfUnitOfWork(db);

        await using (await unitOfWork.BeginAsync(CancellationToken.None))
        {
            Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
            await new RefreshTokenRepository(db, NullLogger<RefreshTokenRepository>.Instance).AddAsync(NewToken(user.Id), CancellationToken.None);

            // Both writes are really in the database for the unit's own connection, so the rollback has something to undo.
            Assert.Equal(1L, await db.Users.CountAsync(u => u.Id == user.Id));
        }

        Assert.Equal(0L, await UsersWithIdAsync(user.Id));
        Assert.Equal(0L, await TokensOfAsync(user.Id));
    }

    [Fact]
    public async Task AnExceptionBetweenTwoWrites_UndoesTheFirst()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        var unitOfWork = new EfUnitOfWork(db);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var unit = await unitOfWork.BeginAsync(CancellationToken.None);
            Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
            throw new InvalidOperationException("the second write failed");
        });

        Assert.Equal(0L, await UsersWithIdAsync(user.Id));
    }

    [Fact]
    public async Task AStatementAndASave_AreUndoneTogether()
    {
        // A save (SaveChangesAsync) and a statement of its own (ExecuteUpdateAsync) both run inside the unit's transaction: neither
        // needs to know about it, which is what keeps the repositories free of any transaction code.
        var owner = NewUser();
        await using var setup = await NewContextAsync();
        Assert.True(await new UserRepository(setup).TryAddAsync(owner, CancellationToken.None));
        await new RefreshTokenRepository(setup, NullLogger<RefreshTokenRepository>.Instance).AddAsync(NewToken(owner.Id), CancellationToken.None);
        var other = NewUser();

        await using (var db = await NewContextAsync())
        await using (await new EfUnitOfWork(db).BeginAsync(CancellationToken.None))
        {
            var revoked = await db.RefreshTokens.Where(token => token.UserId == owner.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, DateTimeOffset.UtcNow), CancellationToken.None);
            Assert.Equal(1, revoked);
            Assert.True(await new UserRepository(db).TryAddAsync(other, CancellationToken.None));
        }

        Assert.Equal(0L, await UsersWithIdAsync(other.Id));
        Assert.Equal(1L, await CountAsync($"select count(*) from refresh_tokens where user_id = '{owner.Id}' and revoked_at is null"));
    }

    [Fact]
    public async Task AUnitCannotBeBegunInsideAnother_BecauseTheyDoNotNest()
    {
        await using var db = await NewContextAsync();
        var unitOfWork = new EfUnitOfWork(db);
        await using var outer = await unitOfWork.BeginAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginAsync(CancellationToken.None));

        Assert.Contains("do not nest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARepositoryThatOpensItsOwnTransaction_CannotRunInsideAUnit()
    {
        // Documents a limit, so it is found here and not in production: RefreshTokenRepository.RevokeFamilyAsync (and TryRotateAsync)
        // begin a transaction of their own, and EF Core cannot nest one. A handler that uses them must not be made transactional.
        await using var db = await NewContextAsync();
        await using var unit = await new EfUnitOfWork(db).BeginAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RefreshTokenRepository(db, NullLogger<RefreshTokenRepository>.Instance)
                .RevokeFamilyAsync(Guid.CreateVersion7(), DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task AfterACommit_TheContextCanBeginAnotherUnit()
    {
        await using var db = await NewContextAsync();
        var unitOfWork = new EfUnitOfWork(db);
        await using (var first = await unitOfWork.BeginAsync(CancellationToken.None))
        {
            await first.CommitAsync(CancellationToken.None);
        }

        await using var second = await unitOfWork.BeginAsync(CancellationToken.None);

        Assert.NotNull(second);
    }
}
