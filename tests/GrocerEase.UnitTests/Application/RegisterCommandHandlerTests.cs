using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class RegisterCommandHandlerTests
{
    private sealed class StubTokenGenerator : IJwtTokenGenerator
    {
        /// <summary>What the stub says every token expires at; a handler must pass it on untouched.</summary>
        public static readonly DateTimeOffset ExpiresAt = new(2026, 11, 13, 10, 10, 0, TimeSpan.Zero);

        public User? LastUser { get; private set; }

        public Role? LastRole { get; private set; }

        public IssuedAccessToken GenerateToken(User user, Role role)
        {
            LastUser = user;
            LastRole = role;
            return new IssuedAccessToken($"token-for-{user.Email}", ExpiresAt);
        }
    }

    // Looks empty when the handler checks, then refuses the add: another registration won the race in between.
    private sealed class RaceLosingRepository : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<User?>(null);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<User?>(null);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> TryChangeRoleAsync(Guid userId, Guid fromRoleId, Guid toRoleId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private readonly RefreshTestKit _refresh = new();

    private static readonly RegisterCommand Ada = new("Ada", "Lovelace", "ada@example.com", "s3cret!");

    [Fact]
    public async Task Handle_ReturnsARefreshToken_WhoseHashIsStoredForTheNewUser()
    {
        var handler = new RegisterCommandHandler(new FakeUserRepository(), new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        var stored = Assert.Single(_refresh.Tokens.Tokens);
        Assert.Equal(result.Value.Id, stored.UserId);
        Assert.Equal(_refresh.Generator.Hash(result.Value.RefreshToken), stored.TokenHash);
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsTaken_StoresNoRefreshToken()
    {
        var repository = new FakeUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);
        await handler.Handle(Ada, CancellationToken.None);

        var second = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Single(_refresh.Tokens.Tokens); // only the first registration started a session
    }

    [Fact]
    public async Task Handle_StoresTheUserAndReturnsItWithAToken()
    {
        var repository = new FakeUserRepository();
        var tokens = new StubTokenGenerator();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(), new FakePasswordHasher(), tokens, _refresh.Issuer);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (user.FirstName, user.LastName, user.Email));
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("token-for-ada@example.com", user.Token);
        Assert.Equal(StubTokenGenerator.ExpiresAt, user.ExpiresAt); // the generator's answer, passed on as it is

        var stored = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(user.Id, stored.Id);
        Assert.Same(stored, tokens.LastUser);
    }

    [Fact]
    public async Task Handle_WhenAnotherRegistrationWinsTheRace_ReturnsADuplicateEmailConflictAndNoToken()
    {
        var tokens = new StubTokenGenerator();
        var handler = new RegisterCommandHandler(new RaceLosingRepository(), new FakeRoleRepository(), new FakePasswordHasher(), tokens, _refresh.Issuer);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.DuplicateEmail, result.Error);
        Assert.Null(tokens.LastUser);
    }

    [Fact]
    public async Task Handle_ReturnsTheEmailInTheFormItWasStoredIn_LowerCase()
    {
        var handler = new RegisterCommandHandler(new FakeUserRepository(), new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        var result = await handler.Handle(Ada with { Email = "Ada@Example.COM" }, CancellationToken.None);

        Assert.Equal("ada@example.com", result.Value.Email);
    }

    [Fact]
    public async Task Handle_GivesTheNewUserTheCustomerRole_NotTheAdminRole()
    {
        var repository = new FakeUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        await handler.Handle(Ada, CancellationToken.None);

        var stored = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);
        Assert.Equal(RoleIds.Customer, stored?.RoleId);
    }

    [Fact]
    public async Task Handle_PutsTheCustomerRoleInTheToken()
    {
        var tokens = new StubTokenGenerator();
        var handler = new RegisterCommandHandler(new FakeUserRepository(), new FakeRoleRepository(), new FakePasswordHasher(), tokens, _refresh.Issuer);

        await handler.Handle(Ada, CancellationToken.None);

        Assert.Equal((RoleIds.Customer, RoleNames.Customer), (tokens.LastRole?.Id, tokens.LastRole?.Name));
        Assert.Equal(tokens.LastUser?.RoleId, tokens.LastRole?.Id); // the role in the token is the one the user was stored with
        Assert.Equal([Permissions.OrdersCancel, Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], tokens.LastRole?.GrantedPermissions, StringComparer.Ordinal); // and it brings what it grants
    }

    [Fact]
    public async Task Handle_WhenTheCustomerRoleIsMissing_ThrowsBecauseTheDatabaseIsBroken_AndStoresNothing()
    {
        var repository = new FakeUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(seeded: false), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(Ada, CancellationToken.None));

        Assert.Contains(RoleNames.Customer, exception.Message, StringComparison.Ordinal);
        Assert.Null(await repository.GetByEmailAsync("ada@example.com", CancellationToken.None));
        Assert.Empty(_refresh.Tokens.Tokens);
    }

    [Fact]
    public async Task Handle_GivesTheNewUserAVersion7Id()
    {
        var handler = new RegisterCommandHandler(new FakeUserRepository(), new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.Equal(7, result.Value.Id.Version);
    }

    [Fact]
    public async Task Handle_StoresTheHashOfThePassword_NotThePassword()
    {
        var repository = new FakeUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        await handler.Handle(Ada, CancellationToken.None);

        var stored = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("hashed:s3cret!", stored.PasswordHash);
        Assert.NotEqual(Ada.Password, stored.PasswordHash, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsTaken_ReturnsADuplicateEmailConflictAndStoresNothingNew()
    {
        var repository = new FakeUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);
        await handler.Handle(Ada, CancellationToken.None);
        var original = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);

        var result = await handler.Handle(Ada with { FirstName = "Other", Email = "ADA@example.com" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.DuplicateEmail, result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Same(original, await repository.GetByEmailAsync("ada@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RejectsNull()
    {
        var handler = new RegisterCommandHandler(new FakeUserRepository(), new FakeRoleRepository(), new FakePasswordHasher(), new StubTokenGenerator(), _refresh.Issuer);

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.Handle(null!, CancellationToken.None));
    }
}
