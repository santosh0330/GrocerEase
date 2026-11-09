using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;

namespace GrocerEase.UnitTests.Application;

public class RegisterCommandHandlerTests
{
    private sealed class StubTokenGenerator : IJwtTokenGenerator
    {
        public User? LastUser { get; private set; }

        public string GenerateToken(User user)
        {
            LastUser = user;
            return $"token-for-{user.Email}";
        }
    }

    // Looks empty when the handler checks, then refuses the add: another registration won the race in between.
    private sealed class RaceLosingRepository : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => Task.FromResult<User?>(null);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static readonly RegisterCommand Ada = new("Ada", "Lovelace", "ada@example.com", "s3cret!");

    [Fact]
    public async Task Handle_StoresTheUserAndReturnsItWithAToken()
    {
        var repository = new InMemoryUserRepository();
        var tokens = new StubTokenGenerator();
        var handler = new RegisterCommandHandler(repository, new FakePasswordHasher(), tokens);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (user.FirstName, user.LastName, user.Email));
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("token-for-ada@example.com", user.Token);

        var stored = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(user.Id, stored.Id);
        Assert.Same(stored, tokens.LastUser);
    }

    [Fact]
    public async Task Handle_WhenAnotherRegistrationWinsTheRace_ReturnsADuplicateEmailConflictAndNoToken()
    {
        var tokens = new StubTokenGenerator();
        var handler = new RegisterCommandHandler(new RaceLosingRepository(), new FakePasswordHasher(), tokens);

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.DuplicateEmail, result.Error);
        Assert.Null(tokens.LastUser);
    }

    [Fact]
    public async Task Handle_ReturnsTheEmailInTheFormItWasStoredIn_LowerCase()
    {
        var handler = new RegisterCommandHandler(new InMemoryUserRepository(), new FakePasswordHasher(), new StubTokenGenerator());

        var result = await handler.Handle(Ada with { Email = "Ada@Example.COM" }, CancellationToken.None);

        Assert.Equal("ada@example.com", result.Value.Email);
    }

    [Fact]
    public async Task Handle_GivesTheNewUserAVersion7Id()
    {
        var handler = new RegisterCommandHandler(new InMemoryUserRepository(), new FakePasswordHasher(), new StubTokenGenerator());

        var result = await handler.Handle(Ada, CancellationToken.None);

        Assert.Equal(7, result.Value.Id.Version);
    }

    [Fact]
    public async Task Handle_StoresTheHashOfThePassword_NotThePassword()
    {
        var repository = new InMemoryUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakePasswordHasher(), new StubTokenGenerator());

        await handler.Handle(Ada, CancellationToken.None);

        var stored = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("hashed:s3cret!", stored.PasswordHash);
        Assert.NotEqual(Ada.Password, stored.PasswordHash, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsTaken_ReturnsADuplicateEmailConflictAndStoresNothingNew()
    {
        var repository = new InMemoryUserRepository();
        var handler = new RegisterCommandHandler(repository, new FakePasswordHasher(), new StubTokenGenerator());
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
        var handler = new RegisterCommandHandler(new InMemoryUserRepository(), new FakePasswordHasher(), new StubTokenGenerator());

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.Handle(null!, CancellationToken.None));
    }
}
