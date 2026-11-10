using GrocerEase.Application.Authentication.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class RefreshTokenIssuerTests
{
    private readonly RefreshTestKit _kit = new();

    [Fact]
    public async Task StartSession_ReturnsThePlainToken_AndStoresOnlyItsHash()
    {
        var userId = Guid.CreateVersion7();

        var token = await _kit.Issuer.StartSessionAsync(userId, CancellationToken.None);

        Assert.Equal("refresh-token-1", token);
        var stored = Assert.Single(_kit.Tokens.Tokens);
        Assert.Equal(_kit.Generator.Hash(token), stored.TokenHash);
        Assert.Equal(userId, stored.UserId);
    }

    [Fact]
    public async Task TheStoredToken_ExpiresInThreeDays_AndItsFamilyInThirty()
    {
        await _kit.Issuer.StartSessionAsync(Guid.CreateVersion7(), CancellationToken.None);

        var stored = Assert.Single(_kit.Tokens.Tokens);
        Assert.Equal(RefreshTestKit.Start, stored.CreatedAt);
        Assert.Equal(RefreshTestKit.Start + TimeSpan.FromDays(3), stored.ExpiresAt);
        Assert.Equal(RefreshTestKit.Start + TimeSpan.FromDays(30), stored.FamilyExpiresAt);
        Assert.Equal(RefreshToken.MaxLifetime, stored.FamilyExpiresAt - stored.CreatedAt);
    }

    [Fact]
    public async Task EachSession_GetsItsOwnTokenAndItsOwnFamily()
    {
        var userId = Guid.CreateVersion7();

        var first = await _kit.Issuer.StartSessionAsync(userId, CancellationToken.None);
        var second = await _kit.Issuer.StartSessionAsync(userId, CancellationToken.None);

        Assert.NotEqual(first, second, StringComparer.Ordinal);
        Assert.Equal(2, _kit.Tokens.Tokens.Select(token => token.FamilyId).Distinct().Count());
    }

    [Fact]
    public async Task NothingStoredContainsThePlainToken()
    {
        var token = await _kit.Issuer.StartSessionAsync(Guid.CreateVersion7(), CancellationToken.None);

        var stored = Assert.Single(_kit.Tokens.Tokens);

        Assert.DoesNotContain(token, Convert.ToHexStringLower(stored.TokenHash), StringComparison.Ordinal);
        Assert.DoesNotContain(token, Convert.ToBase64String(stored.TokenHash), StringComparison.Ordinal);
    }
}
