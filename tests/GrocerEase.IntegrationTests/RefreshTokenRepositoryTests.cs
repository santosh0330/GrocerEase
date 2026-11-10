using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The refresh token repository against a real PostgreSQL, connected as the least-privilege application role, as the API is.
/// The two race tests run real concurrent requests against the real database: only it can show that the ordering holds.
/// </summary>
public class RefreshTokenRepositoryTests
{
    private static readonly TimeSpan ThreeDays = TimeSpan.FromDays(3);
    private static readonly TimeSpan ThirtyDays = TimeSpan.FromDays(30);

    private static byte[] RandomHash() => System.Security.Cryptography.RandomNumberGenerator.GetBytes(RefreshToken.HashLength);

    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    private static async Task<RefreshTokenRepository> NewRepositoryAsync() =>
        new(await NewContextAsync(), NullLogger<RefreshTokenRepository>.Instance);

    private static async Task<Guid> NewUserAsync()
    {
        var user = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "a-password-hash");
        await using var db = await NewContextAsync();
        Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
        return user.Id;
    }

    /// <summary>A real user with the first token of a new family already stored.</summary>
    private static async Task<RefreshToken> StartedAsync(DateTimeOffset? now = null)
    {
        var token = RefreshToken.Start(await NewUserAsync(), RandomHash(), now ?? DateTimeOffset.UtcNow, ThreeDays, ThirtyDays);
        await (await NewRepositoryAsync()).AddAsync(token, CancellationToken.None);
        return token;
    }

    private static async Task<RefreshToken?> FindAsync(byte[] hash) =>
        await (await NewRepositoryAsync()).GetByHashAsync(hash, CancellationToken.None);

    private static async Task<long> LiveTokensInAsync(Guid familyId)
    {
        await using var connection = new NpgsqlConnection(await PostgresServer.ApplicationConnectionStringAsync());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from refresh_tokens where family_id = @family and revoked_at is null", connection);
        command.Parameters.AddWithValue("family", familyId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task AStoredToken_IsFoundByItsHash_WithAllItsValues()
    {
        var token = await StartedAsync();

        var found = await FindAsync(token.TokenHash);

        Assert.NotNull(found);
        Assert.Equal((token.Id, token.UserId, token.FamilyId), (found.Id, found.UserId, found.FamilyId));
        Assert.Equal(token.TokenHash, found.TokenHash);
        // PostgreSQL keeps microseconds, .NET ticks are finer: equal to the millisecond is equal enough.
        var tolerance = TimeSpan.FromMilliseconds(1);
        Assert.Equal(token.CreatedAt.ToUniversalTime(), found.CreatedAt.ToUniversalTime(), tolerance);
        Assert.Equal(token.ExpiresAt.ToUniversalTime(), found.ExpiresAt.ToUniversalTime(), tolerance);
        Assert.Equal(token.FamilyExpiresAt.ToUniversalTime(), found.FamilyExpiresAt.ToUniversalTime(), tolerance);
        Assert.Null(found.ConsumedAt);
        Assert.Null(found.RevokedAt);
        Assert.False(found.IsSpent);
    }

    [Fact]
    public async Task AnUnknownHash_IsNull() => Assert.Null(await FindAsync(RandomHash()));

    [Fact]
    public async Task Rotating_ConsumesTheCurrentToken_AndStoresTheReplacement()
    {
        var current = await StartedAsync();
        var now = DateTimeOffset.UtcNow;
        var replacement = current.Successor(RandomHash(), now, ThreeDays);

        var rotated = await (await NewRepositoryAsync()).TryRotateAsync(current, replacement, now, CancellationToken.None);

        Assert.True(rotated);
        var oldRow = await FindAsync(current.TokenHash);
        Assert.NotNull(oldRow);
        Assert.NotNull(oldRow.ConsumedAt);
        Assert.True(oldRow.IsSpent);
        var newRow = await FindAsync(replacement.TokenHash);
        Assert.NotNull(newRow);
        Assert.Equal(current.FamilyId, newRow.FamilyId);
        Assert.False(newRow.IsSpent);
    }

    [Fact]
    public async Task Rotating_ATokenThatWasAlreadyConsumed_IsRefused_AndStoresNothing()
    {
        var current = await StartedAsync();
        Assert.True(await (await NewRepositoryAsync()).TryRotateAsync(current, current.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays), DateTimeOffset.UtcNow, CancellationToken.None));
        var second = current.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);

        var rotated = await (await NewRepositoryAsync()).TryRotateAsync(current, second, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(rotated);
        Assert.Null(await FindAsync(second.TokenHash));
    }

    [Fact]
    public async Task Rotating_ARevokedToken_IsRefused_AndStoresNothing()
    {
        var current = await StartedAsync();
        await (await NewRepositoryAsync()).RevokeFamilyAsync(current.FamilyId, DateTimeOffset.UtcNow, CancellationToken.None);
        var replacement = current.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);

        var rotated = await (await NewRepositoryAsync()).TryRotateAsync(current, replacement, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(rotated);
        Assert.Null(await FindAsync(replacement.TokenHash));
    }

    [Fact]
    public async Task Rotating_WithAReplacementFromAnotherFamily_Throws()
    {
        var current = await StartedAsync();
        var stranger = await StartedAsync();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await (await NewRepositoryAsync()).TryRotateAsync(current, stranger.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays), DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task RevokingAFamily_RevokesEveryTokenInIt_AndNoOtherFamily()
    {
        var first = await StartedAsync();
        var second = first.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);
        Assert.True(await (await NewRepositoryAsync()).TryRotateAsync(first, second, DateTimeOffset.UtcNow, CancellationToken.None));
        var other = await StartedAsync();

        await (await NewRepositoryAsync()).RevokeFamilyAsync(first.FamilyId, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull((await FindAsync(first.TokenHash))!.RevokedAt);
        Assert.NotNull((await FindAsync(second.TokenHash))!.RevokedAt);
        Assert.Null((await FindAsync(other.TokenHash))!.RevokedAt);
    }

    [Fact]
    public async Task RevokingAFamilyTwice_ChangesNothingTheSecondTime()
    {
        var token = await StartedAsync();
        var repository = await NewRepositoryAsync();
        var firstTime = DateTimeOffset.UtcNow;
        await repository.RevokeFamilyAsync(token.FamilyId, firstTime, CancellationToken.None);

        await (await NewRepositoryAsync()).RevokeFamilyAsync(token.FamilyId, firstTime.AddHours(1), CancellationToken.None);

        Assert.Equal(firstTime.ToUniversalTime(), (await FindAsync(token.TokenHash))!.RevokedAt!.Value.ToUniversalTime(), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task OnlyTheHashIsStored_NeverTheToken()
    {
        var plaintext = "plaintext-refresh-token-canary-" + Guid.NewGuid().ToString("N");
        var token = RefreshToken.Start(await NewUserAsync(), System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext)), DateTimeOffset.UtcNow, ThreeDays, ThirtyDays);
        await (await NewRepositoryAsync()).AddAsync(token, CancellationToken.None);

        await using var connection = new NpgsqlConnection(await PostgresServer.ApplicationConnectionStringAsync());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select t::text from refresh_tokens t where t.id = @id", connection);
        command.Parameters.AddWithValue("id", token.Id);
        var wholeRow = (string)(await command.ExecuteScalarAsync())!;

        Assert.DoesNotContain(plaintext, wholeRow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenManyRequestsPresentTheSameToken_ExactlyOneRotationSucceeds()
    {
        const int concurrentRequests = 12;
        var current = await StartedAsync();
        using var gate = new ManualResetEventSlim(false);

        // One context per request, as in the API. All wait at the gate and go at the same moment.
        var attempts = Enumerable.Range(0, concurrentRequests).Select(_ => Task.Run(async () =>
        {
            var repository = await NewRepositoryAsync();
            var replacement = current.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);
            gate.Wait();
            return (Won: await repository.TryRotateAsync(current, replacement, DateTimeOffset.UtcNow, CancellationToken.None), replacement.TokenHash);
        })).ToList();
        await Task.Delay(500); // let every task reach the gate
        gate.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(result => result.Won));
        var winner = results.Single(result => result.Won);
        Assert.NotNull(await FindAsync(winner.TokenHash));
        foreach (var loser in results.Where(result => !result.Won))
        {
            Assert.Null(await FindAsync(loser.TokenHash)); // a refused rotation left no token behind
        }
    }

    [Fact]
    public async Task ARevocationAndARotationOfTheSameFamilyAtOnce_NeverLeaveALiveToken()
    {
        // The hazard: the revoking statement only sees rows that existed when it began, so a token committed by a rotation at the
        // same moment could stay live after the family was revoked. The family lock makes them take turns. Repeated, to give a
        // missing lock many chances to show.
        for (var round = 0; round < 60; round++)
        {
            var first = await StartedAsync();
            var second = first.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);
            Assert.True(await (await NewRepositoryAsync()).TryRotateAsync(first, second, DateTimeOffset.UtcNow, CancellationToken.None));
            var third = second.Successor(RandomHash(), DateTimeOffset.UtcNow, ThreeDays);
            var rotationRepository = await NewRepositoryAsync();
            var revocationRepository = await NewRepositoryAsync();
            using var gate = new ManualResetEventSlim(false);

            var rotation = Task.Run(() =>
            {
                gate.Wait();
                return rotationRepository.TryRotateAsync(second, third, DateTimeOffset.UtcNow, CancellationToken.None);
            });
            var revocation = Task.Run(() =>
            {
                gate.Wait();
                return revocationRepository.RevokeFamilyAsync(first.FamilyId, DateTimeOffset.UtcNow, CancellationToken.None);
            });
            await Task.Delay(20);
            gate.Set();
            await Task.WhenAll(rotation, revocation);

            Assert.Equal(0, await LiveTokensInAsync(first.FamilyId));
        }
    }
}
