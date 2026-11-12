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
        new(Guid.CreateVersion7(), firstName, "Lovelace", email ?? UniqueEmail(), "a-password-hash", RoleIds.Customer);

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
            (user.Id, user.FirstName, user.LastName, user.Email, user.PasswordHash, user.RoleId),
            (found.Id, found.FirstName, found.LastName, found.Email, found.PasswordHash, found.RoleId));
    }

    [Fact]
    public async Task AUserWhoseRoleDoesNotExist_IsAnException_NotADuplicateEmail()
    {
        var user = new User(Guid.CreateVersion7(), "Ada", "Lovelace", UniqueEmail(), "a-password-hash", Guid.CreateVersion7());
        await using var db = await NewContextAsync();

        // The foreign key refuses the row. That is a bug, not "the email is taken", so it must not come back as false.
        await Assert.ThrowsAsync<DbUpdateException>(() => new UserRepository(db).TryAddAsync(user, CancellationToken.None));
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
    public async Task AUserThatWasAdded_IsFoundById()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        var repository = new UserRepository(db);
        await repository.TryAddAsync(user, CancellationToken.None);

        await using var other = await NewContextAsync();
        var found = await new UserRepository(other).GetByIdAsync(user.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal((user.Email, user.FirstName, user.PasswordHash), (found.Email, found.FirstName, found.PasswordHash));
    }

    [Fact]
    public async Task AnUnknownId_IsNull()
    {
        await using var db = await NewContextAsync();

        Assert.Null(await new UserRepository(db).GetByIdAsync(Guid.CreateVersion7(), CancellationToken.None));
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

        var second = new User(Guid.CreateVersion7(), "Other", "Person", first.Email.ToUpperInvariant(), "another-hash", RoleIds.Customer);
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
        var sameIdOtherEmail = new User(first.Id, "Other", "Person", UniqueEmail(), "another-hash", RoleIds.Customer);
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

    private static async Task<Guid> StoredUserAsync()
    {
        var user = NewUser();
        await using var db = await NewContextAsync();
        Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
        return user.Id;
    }

    private static async Task<bool> ChangeRoleAsync(Guid userId, Guid from, Guid to)
    {
        await using var db = await NewContextAsync();
        return await new UserRepository(db).TryChangeRoleAsync(userId, from, to, CancellationToken.None);
    }

    private static async Task<string> RoleOfAsync(Guid userId) =>
        (await PostgresServer.ScalarAsync<string>(
            await PostgresServer.ApplicationConnectionStringAsync(),
            $"select r.name from users u join roles r on r.id = u.role_id where u.id = '{userId}'"))!;

    [Fact]
    public async Task TheRole_IsChanged_WhenTheUserHoldsTheOneItIsChangedFrom()
    {
        var id = await StoredUserAsync();

        var changed = await ChangeRoleAsync(id, RoleIds.Customer, RoleIds.StoreUser);

        Assert.True(changed);
        Assert.Equal(RoleNames.StoreUser, await RoleOfAsync(id));
    }

    [Fact]
    public async Task TheRole_IsNotChanged_WhenTheUserHoldsAnotherOne()
    {
        var id = await StoredUserAsync();

        var changed = await ChangeRoleAsync(id, RoleIds.Admin, RoleIds.StoreUser); // they are a customer, not an admin

        Assert.False(changed);
        Assert.Equal(RoleNames.Customer, await RoleOfAsync(id));
    }

    [Fact]
    public async Task ChangingTheRoleOfAnUnknownUser_ChangesNothing_AndSaysSo() =>
        Assert.False(await ChangeRoleAsync(Guid.CreateVersion7(), RoleIds.Customer, RoleIds.StoreUser));

    [Fact]
    public async Task ARoleThatDoesNotExist_IsAnException_NotAnOrdinaryRefusal()
    {
        var id = await StoredUserAsync();

        // The foreign key refuses it. That is a bug in the caller, so it must not come back as false ("they hold another role").
        var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => ChangeRoleAsync(id, RoleIds.Customer, Guid.CreateVersion7()));

        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_users_roles_role_id", exception.ConstraintName);
        Assert.Equal(RoleNames.Customer, await RoleOfAsync(id));
    }

    [Fact]
    public async Task TheSameChangeTwice_SucceedsOnce_BecauseTheSecondFindsTheRoleAlreadyChanged()
    {
        var id = await StoredUserAsync();

        Assert.True(await ChangeRoleAsync(id, RoleIds.Customer, RoleIds.StoreUser));
        Assert.False(await ChangeRoleAsync(id, RoleIds.Customer, RoleIds.StoreUser));
    }

    [Fact]
    public async Task ManyRequestsChangingTheSameCustomerAtTheSameTime_OneWins()
    {
        var id = await StoredUserAsync();

        // Eight requests that all saw a customer, each trying to make them something else. Only the statement's own condition can
        // decide: a read followed by a write would let several of them through.
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(attempt => ChangeRoleAsync(id, RoleIds.Customer, attempt % 2 == 0 ? RoleIds.StoreUser : RoleIds.Admin)));

        Assert.Equal(1, results.Count(changed => changed));
        Assert.Contains(await RoleOfAsync(id), new[] { RoleNames.StoreUser, RoleNames.Admin }, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ChangingTheRole_MovesUpdatedAt_BecauseTheDatabaseKeepsIt()
    {
        var id = await StoredUserAsync();
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        await PostgresServer.ExecuteAsync(connection, "select pg_sleep(0.05)"); // so a moved timestamp cannot equal the created one

        await ChangeRoleAsync(id, RoleIds.Customer, RoleIds.StoreUser);

        Assert.True(await PostgresServer.ScalarAsync<bool>(connection, $"select updated_at > created_at from users where id = '{id}'"));
    }
}
