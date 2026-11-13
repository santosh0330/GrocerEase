using GrocerEase.Application.Authentication.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.UnitTests.Infrastructure;

public class JwtTokenGeneratorTests
{
    private const string Key = "unit-test-signing-key-0123456789-abcdef";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JwtSettings Settings(string key = Key, int expiryMinutes = 60) => new()
    {
        SecretKey = key,
        Issuer = "issuer-x",
        Audience = "audience-x",
        ExpiryMinutes = expiryMinutes,
    };

    private static JwtTokenGenerator Generator(DateTimeOffset? now = null, JwtSettings? settings = null) =>
        new(Options.Create(settings ?? Settings()), new FixedTimeProvider(now ?? DateTimeOffset.UtcNow));

    private static User Ada() => new(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", "a-password-hash", RoleIds.Customer);

    private static Role RoleWith(Guid id, string name, params string[] permissions)
    {
        var role = new Role(id, name);
        foreach (var permission in permissions)
        {
            role.Grant(permission);
        }

        return role;
    }

    // What the database is seeded with, written out: a customer reads its profile, an admin also reads the roles.
    private static readonly Role CustomerRole = RoleWith(RoleIds.Customer, RoleNames.Customer, Permissions.ProfileRead);
    private static readonly Role AdminRole = RoleWith(RoleIds.Admin, RoleNames.Admin, Permissions.RolesRead, Permissions.ProfileRead);

    private static string[] PermissionsIn(JsonWebToken jwt) =>
        [.. jwt.Claims.Where(claim => string.Equals(claim.Type, JwtClaims.Permission, StringComparison.Ordinal)).Select(claim => claim.Value)];

    private static TokenValidationParameters Rules(string key = Key, string issuer = "issuer-x", string audience = "audience-x") => new()
    {
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    };

    private static Task<TokenValidationResult> ValidateAsync(string token, TokenValidationParameters rules) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, rules);

    [Fact]
    public async Task GenerateToken_ReturnsATokenThatValidatesWithTheSameKeyIssuerAndAudience()
    {
        var token = Generator().GenerateToken(Ada(), CustomerRole).Token;

        var result = await ValidateAsync(token, Rules());

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public void GenerateToken_IsSignedWithHmacSha256()
    {
        var jwt = new JsonWebToken(Generator().GenerateToken(Ada(), CustomerRole).Token);

        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public void GenerateToken_CarriesTheUsersIdentityClaims()
    {
        var user = Ada();

        var jwt = new JsonWebToken(Generator().GenerateToken(user, CustomerRole).Token);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("ada@example.com", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Ada", jwt.GetClaim(JwtRegisteredClaimNames.GivenName).Value);
        Assert.Equal("Lovelace", jwt.GetClaim(JwtRegisteredClaimNames.FamilyName).Value);
        Assert.Equal("issuer-x", jwt.Issuer);
        Assert.Equal("audience-x", Assert.Single(jwt.Audiences));
    }

    [Fact]
    public void GenerateToken_CarriesTheNameOfTheRoleTheUserHolds()
    {
        var customer = new JsonWebToken(Generator().GenerateToken(Ada(), CustomerRole).Token);
        var admin = new JsonWebToken(Generator().GenerateToken(
            new User(Guid.CreateVersion7(), "Grace", "Hopper", "grace@example.com", "hash", RoleIds.Admin), AdminRole).Token);

        Assert.Equal("Customer", customer.GetClaim(JwtClaims.Role).Value);
        Assert.Equal("Admin", admin.GetClaim(JwtClaims.Role).Value);
    }

    [Fact]
    public void GenerateToken_CarriesWhatTheRoleMayDo_OneEntryPerPermission_InAStableOrder()
    {
        var customer = new JsonWebToken(Generator().GenerateToken(Ada(), CustomerRole).Token);
        var admin = new JsonWebToken(Generator().GenerateToken(
            new User(Guid.CreateVersion7(), "Grace", "Hopper", "grace@example.com", "hash", RoleIds.Admin), AdminRole).Token);

        Assert.Equal([Permissions.ProfileRead], PermissionsIn(customer), StringComparer.Ordinal);
        Assert.Equal([Permissions.ProfileRead, Permissions.RolesRead], PermissionsIn(admin), StringComparer.Ordinal); // sorted, though granted the other way round
    }

    [Fact]
    public void GenerateToken_WritesThePermissionsAsAJsonArray_EvenForOne()
    {
        var jwt = new JsonWebToken(Generator().GenerateToken(Ada(), CustomerRole).Token);

        Assert.Equal(System.Text.Json.JsonValueKind.Array, jwt.GetPayloadValue<System.Text.Json.JsonElement>(JwtClaims.Permission).ValueKind);
    }

    [Fact]
    public void GenerateToken_ForARoleThatGrantsNothing_HasNoPermissionClaim()
    {
        var nothing = RoleWith(RoleIds.Customer, RoleNames.Customer);

        var jwt = new JsonWebToken(Generator().GenerateToken(Ada(), nothing).Token);

        Assert.False(jwt.TryGetClaim(JwtClaims.Permission, out _));
        Assert.Equal("Customer", jwt.GetClaim(JwtClaims.Role).Value); // the role is still named
    }

    [Fact]
    public void GenerateToken_LeavesOtherKindsOfClaimOutOfThePermissions()
    {
        var role = RoleWith(RoleIds.Customer, RoleNames.Customer, Permissions.ProfileRead);
        role.AddClaim("department", "kitchen");

        var jwt = new JsonWebToken(Generator().GenerateToken(Ada(), role).Token);

        Assert.Equal([Permissions.ProfileRead], PermissionsIn(jwt), StringComparer.Ordinal);
    }

    [Fact]
    public void GenerateToken_EvenForTheLongestEmailAndNames_FitsWhatTheRefreshEndpointAccepts()
    {
        // An access token sent as a refresh token must be turned away like any other wrong token, so it has to be short enough
        // to reach the lookup. The longest values the entities allow give the longest token a user can have.
        var user = new User(
            Guid.CreateVersion7(), new string('a', User.MaxNameLength), new string('b', User.MaxNameLength),
            new string('c', User.MaxEmailLength - 12) + "@example.com", "hash", RoleIds.Admin);

        var token = Generator().GenerateToken(user, AdminRole).Token;

        Assert.InRange(token.Length, 1, GrocerEase.Application.Authentication.Validators.RefreshCommandValidator.MaxRefreshTokenLength);
    }

    [Fact]
    public void GenerateToken_WritesThePermissionsUnderTheNameTheClaimsAreStoredAs() =>
        Assert.Equal(Permissions.ClaimType, JwtClaims.Permission);

    [Fact]
    public void GenerateToken_WritesTheRoleUnderTheNameTheValidationParametersReadItBy() =>
        Assert.Equal(JwtClaims.Role, JwtValidationParameters.From(Settings()).RoleClaimType);

    [Fact]
    public void GenerateToken_RefusesARoleThatIsNotTheOneTheUserHolds()
    {
        var customer = Ada(); // holds Customer

        var exception = Assert.Throws<ArgumentException>(() => Generator().GenerateToken(customer, AdminRole).Token);

        Assert.Contains("Admin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateToken_NeverContainsThePasswordHash()
    {
        var user = Ada();

        var jwt = new JsonWebToken(Generator().GenerateToken(user, CustomerRole).Token);

        Assert.DoesNotContain(jwt.Claims, claim => claim.Value.Contains(user.PasswordHash, StringComparison.Ordinal));
        Assert.False(jwt.TryGetClaim("password", out _));
        Assert.False(jwt.TryGetClaim("passwordHash", out _));
    }

    [Fact]
    public void GenerateToken_ExpiresAfterTheConfiguredMinutes()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var jwt = new JsonWebToken(Generator(now, Settings(expiryMinutes: 15)).GenerateToken(Ada(), CustomerRole).Token);

        Assert.Equal(now.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(1440)]
    public void GenerateToken_SaysItExpiresAtTheExactInstantItsExpClaimHolds_WhateverTheSetting(int minutes)
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var issued = Generator(now, Settings(expiryMinutes: minutes)).GenerateToken(Ada(), CustomerRole);

        var exp = new JsonWebToken(issued.Token).GetPayloadValue<long>(JwtRegisteredClaimNames.Exp);
        Assert.Equal(now.AddMinutes(minutes), issued.ExpiresAt);
        Assert.Equal(issued.ExpiresAt.ToUnixTimeSeconds(), exp);
    }

    [Fact]
    public void GenerateToken_WithAClockThatHasFractionsOfASecond_StillSaysTheWholeSecondTheTokenHolds()
    {
        // A JWT cannot say a fraction of a second. Were the response to carry 12:10:00.9 and the token 12:10:00, a client that trusted the response
        // would believe the token lived almost a second longer than it does.
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, 900, TimeSpan.Zero);

        var issued = Generator(now, Settings(expiryMinutes: 10)).GenerateToken(Ada(), CustomerRole);

        Assert.Equal(new DateTimeOffset(2030, 1, 1, 12, 10, 0, TimeSpan.Zero), issued.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, issued.ExpiresAt.Offset);
        Assert.Equal(issued.ExpiresAt.ToUnixTimeSeconds(), new JsonWebToken(issued.Token).GetPayloadValue<long>(JwtRegisteredClaimNames.Exp));
    }

    [Fact]
    public void GenerateToken_ExpiryMovesWithTheSetting()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var soon = Generator(now, Settings(expiryMinutes: 5)).GenerateToken(Ada(), CustomerRole);
        var later = Generator(now, Settings(expiryMinutes: 30)).GenerateToken(Ada(), CustomerRole);

        Assert.Equal(TimeSpan.FromMinutes(25), later.ExpiresAt - soon.ExpiresAt);
    }

    [Fact]
    public void IssuedAccessToken_ToString_DoesNotContainTheToken_ButSaysWhenItExpires()
    {
        var text = new IssuedAccessToken("jwt-value", new DateTimeOffset(2030, 1, 1, 12, 10, 0, TimeSpan.Zero)).ToString();

        Assert.DoesNotContain("jwt-value", text, StringComparison.Ordinal);
        Assert.Contains("2030-01-01T12:10:00", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedOnceItHasExpired()
    {
        var issuedTwoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);

        var token = Generator(issuedTwoHoursAgo, Settings(expiryMinutes: 60)).GenerateToken(Ada(), CustomerRole).Token;
        var result = await ValidateAsync(token, Rules());

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedWithAnotherKey()
    {
        var token = Generator().GenerateToken(Ada(), CustomerRole).Token;

        var result = await ValidateAsync(token, Rules(key: "a-completely-different-signing-key-9876543210"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedForAnotherAudienceOrIssuer()
    {
        var token = Generator().GenerateToken(Ada(), CustomerRole).Token;

        Assert.False((await ValidateAsync(token, Rules(audience: "someone-else"))).IsValid);
        Assert.False((await ValidateAsync(token, Rules(issuer: "someone-else"))).IsValid);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedWhenItsPayloadIsTamperedWith()
    {
        var token = Generator().GenerateToken(Ada(), CustomerRole).Token;
        var parts = token.Split('.');
        var forgedPayload = Base64UrlEncoder.Encode("""{"sub":"someone-else","iss":"issuer-x","aud":"audience-x","exp":9999999999}""");

        var result = await ValidateAsync($"{parts[0]}.{forgedPayload}.{parts[2]}", Rules());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void GenerateToken_GivesEveryTokenItsOwnId()
    {
        var generator = Generator();
        var user = Ada();

        var first = new JsonWebToken(generator.GenerateToken(user, CustomerRole).Token);
        var second = new JsonWebToken(generator.GenerateToken(user, CustomerRole).Token);

        Assert.NotEqual(first.Id, second.Id, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_AndGenerateToken_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new JwtTokenGenerator(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new JwtTokenGenerator(Options.Create(Settings()), null!));
        Assert.Throws<ArgumentNullException>(() => Generator().GenerateToken(null!, CustomerRole).Token);
        Assert.Throws<ArgumentNullException>(() => Generator().GenerateToken(Ada(), null!).Token);
    }
}
