using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class LoginCommandHandlerTests
{
    private sealed class StubTokenGenerator : IJwtTokenGenerator
    {
        public string GenerateToken(User user) => $"token-for-{user.Email}";
    }

    private readonly RefreshTestKit _refresh = new();

    private async Task<(LoginCommandHandler Handler, User Ada, FakePasswordHasher Hasher)> HandlerWithAdaAsync()
    {
        var repository = new FakeUserRepository();
        var hasher = new FakePasswordHasher();
        var ada = new User(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", hasher.Hash("s3cret!"));
        Assert.True(await repository.TryAddAsync(ada, CancellationToken.None));
        return (new LoginCommandHandler(repository, hasher, new StubTokenGenerator(), _refresh.Issuer), ada, hasher);
    }

    [Fact]
    public async Task Handle_WithTheRightPassword_ReturnsTheStoredUserWithAToken()
    {
        var (handler, ada, _) = await HandlerWithAdaAsync();

        var result = await handler.Handle(new LoginCommand("ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = result.Value;
        Assert.Equal(ada.Id, user.Id);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (user.FirstName, user.LastName, user.Email));
        Assert.Equal("token-for-ada@example.com", user.Token);
    }

    [Fact]
    public async Task Handle_WithTheRightPassword_ReturnsARefreshToken_WhoseHashIsStoredForTheUser()
    {
        var (handler, ada, _) = await HandlerWithAdaAsync();

        var result = await handler.Handle(new LoginCommand("ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        var stored = Assert.Single(_refresh.Tokens.Tokens);
        Assert.Equal(ada.Id, stored.UserId);
        Assert.Equal(_refresh.Generator.Hash(result.Value.RefreshToken), stored.TokenHash);
    }

    [Fact]
    public async Task Handle_EachLogin_StartsItsOwnSession()
    {
        var (handler, _, _) = await HandlerWithAdaAsync();

        var first = await handler.Handle(new LoginCommand("ada@example.com", "s3cret!"), CancellationToken.None);
        var second = await handler.Handle(new LoginCommand("ada@example.com", "s3cret!"), CancellationToken.None);

        Assert.NotEqual(first.Value.RefreshToken, second.Value.RefreshToken, StringComparer.Ordinal);
        Assert.Equal(2, _refresh.Tokens.Tokens.Select(token => token.FamilyId).Distinct().Count());
    }

    [Theory]
    [InlineData("ada@example.com", "wrong")]
    [InlineData("nobody@example.com", "s3cret!")]
    public async Task Handle_WithBadCredentials_StoresNoRefreshToken(string email, string password)
    {
        var (handler, _, _) = await HandlerWithAdaAsync();

        await handler.Handle(new LoginCommand(email, password), CancellationToken.None);

        Assert.Empty(_refresh.Tokens.Tokens);
    }

    [Fact]
    public async Task Handle_IgnoresTheCaseOfTheEmail()
    {
        var (handler, ada, _) = await HandlerWithAdaAsync();

        var result = await handler.Handle(new LoginCommand("ADA@Example.com", "s3cret!"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ada.Id, result.Value.Id);
    }

    [Theory]
    [InlineData("ada@example.com", "wrong")]
    [InlineData("ada@example.com", "S3CRET!")]
    [InlineData("nobody@example.com", "s3cret!")]
    public async Task Handle_WithAWrongPasswordOrUnknownEmail_ReturnsTheSameInvalidCredentialsError(string email, string password)
    {
        var (handler, _, _) = await HandlerWithAdaAsync();

        var result = await handler.Handle(new LoginCommand(email, password), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task Handle_WithAnUnknownEmail_StillRunsAVerifyWithNoHash_SoTheTimingDoesNotGiveItAway()
    {
        var (handler, _, hasher) = await HandlerWithAdaAsync();

        await handler.Handle(new LoginCommand("nobody@example.com", "s3cret!"), CancellationToken.None);

        Assert.Equal([(string?)null], hasher.VerifiedHashes);
    }

    [Fact]
    public async Task Handle_RejectsNull()
    {
        var (handler, _, _) = await HandlerWithAdaAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.Handle(null!, CancellationToken.None));
    }
}
