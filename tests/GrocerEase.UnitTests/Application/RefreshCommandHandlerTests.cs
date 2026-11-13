using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class RefreshCommandHandlerTests
{
    private sealed class StubTokenGenerator : IJwtTokenGenerator
    {
        /// <summary>What the stub says every token expires at; a handler must pass it on untouched.</summary>
        public static readonly DateTimeOffset ExpiresAt = new(2026, 11, 13, 10, 10, 0, TimeSpan.Zero);

        private int _count;

        public Role? LastRole { get; private set; }

        public IssuedAccessToken GenerateToken(User user, Role role)
        {
            LastRole = role;
            return new IssuedAccessToken($"access-{Interlocked.Increment(ref _count)}-for-{user.Email}", ExpiresAt);
        }
    }

    /// <summary>Another request consumes the token between the handler's read and its rotation: the rotation comes back refused.</summary>
    private sealed class LosesTheRace(FakeRefreshTokenRepository inner) : IRefreshTokenRepository
    {
        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken) => inner.AddAsync(token, cancellationToken);

        public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
            inner.GetByHashAsync(tokenHash, cancellationToken);

        public async Task<bool> TryRotateAsync(RefreshToken current, RefreshToken replacement, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var other = current.Successor(new byte[RefreshToken.HashLength], now, TimeSpan.FromDays(3));
            await inner.TryRotateAsync(current, other, now, cancellationToken); // the other request wins
            return await inner.TryRotateAsync(current, replacement, now, cancellationToken); // ours is refused
        }

        public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
            inner.RevokeFamilyAsync(familyId, now, cancellationToken);
    }

    private readonly RefreshTestKit _kit = new();
    private readonly FakeUserRepository _users = new();
    private readonly StubTokenGenerator _tokens = new();
    private readonly User _ada = new(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", "hashed:s3cret!", RoleIds.Customer);

    private RefreshCommandHandler Handler(IRefreshTokenRepository? tokens = null, IUserRepository? users = null, IRoleRepository? roles = null) =>
        new(tokens ?? _kit.Tokens, _kit.Generator, _kit.Policy, users ?? _users, roles ?? new FakeRoleRepository(), _tokens, _kit.Clock);

    /// <summary>Ada exists and has signed in: her first refresh token is returned.</summary>
    private async Task<string> SignedInAsync()
    {
        Assert.True(await _users.TryAddAsync(_ada, CancellationToken.None));
        return await _kit.Issuer.StartSessionAsync(_ada.Id, CancellationToken.None);
    }

    private static void AssertInvalid(Result<AuthenticationResult> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken, result.Error);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task Handle_WithAValidToken_ReturnsTheUserWithANewAccessTokenAndANewRefreshToken()
    {
        var token = await SignedInAsync();

        var result = await Handler().Handle(new RefreshCommand(token), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((_ada.Id, "Ada", "Lovelace", "ada@example.com"), (result.Value.Id, result.Value.FirstName, result.Value.LastName, result.Value.Email));
        Assert.Equal("access-1-for-ada@example.com", result.Value.Token);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        Assert.NotEqual(token, result.Value.RefreshToken, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_PutsTheRoleTheUserHoldsNow_InTheNewAccessToken()
    {
        var token = await SignedInAsync();

        await Handler().Handle(new RefreshCommand(token), CancellationToken.None);

        Assert.Equal((RoleIds.Customer, RoleNames.Customer), (_tokens.LastRole?.Id, _tokens.LastRole?.Name));
        Assert.Equal([Permissions.OrdersCancel, Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], _tokens.LastRole?.GrantedPermissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_WhenTheUsersRoleDoesNotExist_ThrowsBecauseTheDatabaseIsBroken()
    {
        var token = await SignedInAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler(roles: new FakeRoleRepository(seeded: false)).Handle(new RefreshCommand(token), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ConsumesThePresentedToken_AndStoresOnlyTheHashOfTheNewOne()
    {
        var token = await SignedInAsync();

        var result = await Handler().Handle(new RefreshCommand(token), CancellationToken.None);

        var old = await _kit.Tokens.GetByHashAsync(_kit.Generator.Hash(token), CancellationToken.None);
        Assert.NotNull(old?.ConsumedAt);
        var replacement = await _kit.Tokens.GetByHashAsync(_kit.Generator.Hash(result.Value.RefreshToken), CancellationToken.None);
        Assert.NotNull(replacement);
        Assert.Null(replacement.ConsumedAt);
        Assert.Equal(old.FamilyId, replacement.FamilyId);
        Assert.Equal(2, _kit.Tokens.Tokens.Count);
    }

    [Fact]
    public async Task TheNewToken_GetsAFreshThreeDayWindow_FromTheMomentOfTheRefresh()
    {
        var token = await SignedInAsync();
        _kit.Clock.Advance(TimeSpan.FromDays(2));

        var result = await Handler().Handle(new RefreshCommand(token), CancellationToken.None);

        var replacement = await _kit.Tokens.GetByHashAsync(_kit.Generator.Hash(result.Value.RefreshToken), CancellationToken.None);
        Assert.Equal(RefreshTestKit.Start + TimeSpan.FromDays(5), replacement!.ExpiresAt);
    }

    [Fact]
    public async Task ATokenIsJustValid_OneTickBeforeItsExpiry_AndExpiredAtIt()
    {
        var token = await SignedInAsync();
        _kit.Clock.Advance(TimeSpan.FromDays(3) - TimeSpan.FromTicks(1));
        Assert.True((await Handler().Handle(new RefreshCommand(token), CancellationToken.None)).IsSuccess);

        var second = await SignedInAsync2();
        _kit.Clock.Advance(TimeSpan.FromDays(3));

        AssertInvalid(await Handler().Handle(new RefreshCommand(second), CancellationToken.None));
    }

    // A second sign-in for the same user, for tests that need a second family.
    private async Task<string> SignedInAsync2() => await _kit.Issuer.StartSessionAsync(_ada.Id, CancellationToken.None);

    [Fact]
    public async Task Handle_WithAnExpiredToken_Fails_RotatesNothing_AndRevokesNothing()
    {
        var token = await SignedInAsync();
        _kit.Clock.Advance(TimeSpan.FromDays(3) + TimeSpan.FromSeconds(1));

        var result = await Handler().Handle(new RefreshCommand(token), CancellationToken.None);

        AssertInvalid(result);
        Assert.Single(_kit.Tokens.Tokens);
        Assert.Empty(_kit.Tokens.RevokedFamilies); // expiry is not theft
    }

    [Fact]
    public async Task Handle_WithAnUnknownToken_Fails_AndRevokesNothing()
    {
        await SignedInAsync();

        var result = await Handler().Handle(new RefreshCommand("not-a-token-the-server-issued"), CancellationToken.None);

        AssertInvalid(result);
        Assert.Empty(_kit.Tokens.RevokedFamilies);
    }

    [Fact]
    public async Task Handle_WhenAnOldTokenIsUsedAgain_Fails_AndRevokesTheWholeFamily()
    {
        var first = await SignedInAsync();
        var second = (await Handler().Handle(new RefreshCommand(first), CancellationToken.None)).Value.RefreshToken;

        var reuse = await Handler().Handle(new RefreshCommand(first), CancellationToken.None); // the thief, or the owner's old copy

        AssertInvalid(reuse);
        var family = _kit.Tokens.Tokens[0].FamilyId;
        Assert.Equal([family], _kit.Tokens.RevokedFamilies);
        Assert.All(_kit.Tokens.Tokens, token => Assert.NotNull(token.RevokedAt));
        AssertInvalid(await Handler().Handle(new RefreshCommand(second), CancellationToken.None)); // the newest token is dead too
    }

    [Fact]
    public async Task Handle_WhenAnOldTokenIsUsedAgain_LeavesOtherSessionsOfTheSameUserAlone()
    {
        var phone = await SignedInAsync();
        var laptop = await SignedInAsync2();
        await Handler().Handle(new RefreshCommand(phone), CancellationToken.None);

        AssertInvalid(await Handler().Handle(new RefreshCommand(phone), CancellationToken.None)); // phone token stolen and replayed

        Assert.True((await Handler().Handle(new RefreshCommand(laptop), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Handle_WithATokenOfARevokedFamily_Fails()
    {
        var token = await SignedInAsync();
        await _kit.Tokens.RevokeFamilyAsync(_kit.Tokens.Tokens[0].FamilyId, _kit.Clock.GetUtcNow(), CancellationToken.None);

        AssertInvalid(await Handler().Handle(new RefreshCommand(token), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_WhenTheTokenWasTakenBetweenTheCheckAndTheRotation_FailsAndRevokesTheFamily()
    {
        var token = await SignedInAsync();

        var result = await Handler(new LosesTheRace(_kit.Tokens)).Handle(new RefreshCommand(token), CancellationToken.None);

        AssertInvalid(result);
        Assert.Single(_kit.Tokens.RevokedFamilies);
        Assert.All(_kit.Tokens.Tokens, stored => Assert.NotNull(stored.RevokedAt));
    }

    [Fact]
    public async Task Handle_WhenTheUserNoLongerExists_FailsAndLeavesTheTokenUnused()
    {
        var token = await SignedInAsync();

        var result = await Handler(users: new FakeUserRepository()).Handle(new RefreshCommand(token), CancellationToken.None);

        AssertInvalid(result);
        Assert.Null(_kit.Tokens.Tokens.Single().ConsumedAt);
    }

    [Fact]
    public async Task ASessionRefreshedEveryTwoDays_StillEndsOnTheThirtiethDay()
    {
        var token = await SignedInAsync();
        var signedInAt = _kit.Clock.GetUtcNow();

        // Day 2, 4, ... 28: each refresh is accepted and starts a new three-day window.
        for (var day = 2; day <= 28; day += 2)
        {
            _kit.Clock.Advance(TimeSpan.FromDays(2));
            var result = await Handler().Handle(new RefreshCommand(token), CancellationToken.None);
            Assert.True(result.IsSuccess, $"day {day} should still refresh");
            token = result.Value.RefreshToken;
        }

        // The token made on day 28 would have run to day 31: the family's end cuts it to day 30.
        var last = await _kit.Tokens.GetByHashAsync(_kit.Generator.Hash(token), CancellationToken.None);
        Assert.Equal(signedInAt + TimeSpan.FromDays(30), last!.ExpiresAt);
        Assert.All(_kit.Tokens.Tokens, stored => Assert.True(stored.ExpiresAt - signedInAt <= TimeSpan.FromDays(30)));

        _kit.Clock.Advance(TimeSpan.FromDays(2)); // day 30
        AssertInvalid(await Handler().Handle(new RefreshCommand(token), CancellationToken.None));
    }

    [Fact]
    public async Task WhenManyRequestsPresentTheSameToken_ExactlyOneSucceeds()
    {
        var token = await SignedInAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => Handler().Handle(new RefreshCommand(token), CancellationToken.None))));

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.All(results.Where(result => result.IsFailure), AssertInvalid);
    }

    [Fact]
    public async Task Handle_RejectsNull() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => Handler().Handle(null!, CancellationToken.None));
}
