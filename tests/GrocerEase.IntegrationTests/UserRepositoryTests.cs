using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The user repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is.
/// Every test uses its own emails, so they can share the database.
/// </summary>
public class UserRepositoryTests
{
    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static User NewUser(string? email = null, string firstName = "Ada") =>
        new(Guid.CreateVersion7(), firstName, "Lovelace", email ?? UniqueEmail(), "a-password-hash");

    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    [Fact]
    public async Task AUserThatWasAdded_IsFoundWithAllItsValues()
    {
        var user = NewUser();
        await using (var db = await NewContextAsync())
        {
            Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
        }

        await using var other = await NewContextAsync(); // a new context, so nothing is served from memory
        var found = await new UserRepository(other).GetByEmailAsync(user.Email, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(
            (user.Id, user.FirstName, user.LastName, user.Email, user.PasswordHash),
            (found.Id, found.FirstName, found.LastName, found.Email, found.PasswordHash));
    }

    [Fact]
    public async Task TheLookup_IgnoresCaseAndPadding()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        var repository = new UserRepository(db);
        await repository.TryAddAsync(user, CancellationToken.None);

        var found = await repository.GetByEmailAsync($"  {user.Email.ToUpperInvariant()} ", CancellationToken.None);

        Assert.Equal(user.Id, found?.Id);
    }

    [Fact]
    public async Task AnUnknownEmail_IsNull()
    {
        await using var db = await NewContextAsync();

        Assert.Null(await new UserRepository(db).GetByEmailAsync(UniqueEmail(), CancellationToken.None));
    }

    [Fact]
    public async Task AddingTheSameEmailAgain_ReturnsFalse_EvenInAnotherCase_AndKeepsTheFirstUser()
    {
        var first = NewUser();
        await using var db = await NewContextAsync();
        var repository = new UserRepository(db);
        Assert.True(await repository.TryAddAsync(first, CancellationToken.None));

        var second = new User(Guid.CreateVersion7(), "Other", "Person", first.Email.ToUpperInvariant(), "another-hash");
        Assert.False(await repository.TryAddAsync(second, CancellationToken.None));

        await using var check = await NewContextAsync();
        var stored = await new UserRepository(check).GetByEmailAsync(first.Email, CancellationToken.None);
        Assert.Equal(first.Id, stored?.Id);
        Assert.Equal("Ada", stored?.FirstName);
    }

    [Fact]
    public async Task AfterARefusedAdd_TheSameContextCanStillStoreAnotherUser()
    {
        var first = NewUser();
        await using var db = await NewContextAsync();
        var repository = new UserRepository(db);
        await repository.TryAddAsync(first, CancellationToken.None);
        Assert.False(await repository.TryAddAsync(NewUser(first.Email), CancellationToken.None));

        var another = NewUser();

        Assert.True(await repository.TryAddAsync(another, CancellationToken.None));
        Assert.NotNull(await repository.GetByEmailAsync(another.Email, CancellationToken.None));
    }

    [Fact]
    public async Task ManyRegistrationsOfOneEmailAtOnce_LetExactlyOneThrough()
    {
        var email = UniqueEmail();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = await NewContextAsync();
            return await new UserRepository(db).TryAddAsync(NewUser(email), CancellationToken.None);
        }));

        Assert.Equal(1, results.Count(added => added));
    }

    [Fact]
    public async Task ADuplicateId_IsNotMistakenForADuplicateEmail_ItIsAnError()
    {
        var first = NewUser();
        var sameIdOtherEmail = new User(first.Id, "Other", "Person", UniqueEmail(), "another-hash");
        await using (var db = await NewContextAsync())
        {
            await new UserRepository(db).TryAddAsync(first, CancellationToken.None);
        }

        // A different context, as a different request would have: it does not know the first user, so the clash is found by
        // the database's primary key, not by EF. It must surface as an error, not as "that email is taken".
        await using var other = await NewContextAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => new UserRepository(other).TryAddAsync(sameIdOtherEmail, CancellationToken.None));
    }

    [Fact]
    public async Task NullArguments_AreRejected()
    {
        await using var db = await NewContextAsync();
        var repository = new UserRepository(db);

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.GetByEmailAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.TryAddAsync(null!, CancellationToken.None));
    }
}
