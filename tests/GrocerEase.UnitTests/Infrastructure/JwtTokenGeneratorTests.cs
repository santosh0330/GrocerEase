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

    private static User Ada() => new(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", "a-password-hash");

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
        var token = Generator().GenerateToken(Ada());

        var result = await ValidateAsync(token, Rules());

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public void GenerateToken_IsSignedWithHmacSha256()
    {
        var jwt = new JsonWebToken(Generator().GenerateToken(Ada()));

        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public void GenerateToken_CarriesTheUsersIdentityClaims()
    {
        var user = Ada();

        var jwt = new JsonWebToken(Generator().GenerateToken(user));

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("ada@example.com", jwt.GetClaim(JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Ada", jwt.GetClaim(JwtRegisteredClaimNames.GivenName).Value);
        Assert.Equal("Lovelace", jwt.GetClaim(JwtRegisteredClaimNames.FamilyName).Value);
        Assert.Equal("issuer-x", jwt.Issuer);
        Assert.Equal("audience-x", Assert.Single(jwt.Audiences));
    }

    [Fact]
    public void GenerateToken_NeverContainsThePasswordHash()
    {
        var user = Ada();

        var jwt = new JsonWebToken(Generator().GenerateToken(user));

        Assert.DoesNotContain(jwt.Claims, claim => claim.Value.Contains(user.PasswordHash, StringComparison.Ordinal));
        Assert.False(jwt.TryGetClaim("password", out _));
        Assert.False(jwt.TryGetClaim("passwordHash", out _));
    }

    [Fact]
    public void GenerateToken_ExpiresAfterTheConfiguredMinutes()
    {
        var now = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var jwt = new JsonWebToken(Generator(now, Settings(expiryMinutes: 15)).GenerateToken(Ada()));

        Assert.Equal(now.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.Equal(now.UtcDateTime, jwt.ValidFrom);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedOnceItHasExpired()
    {
        var issuedTwoHoursAgo = DateTimeOffset.UtcNow.AddHours(-2);

        var token = Generator(issuedTwoHoursAgo, Settings(expiryMinutes: 60)).GenerateToken(Ada());
        var result = await ValidateAsync(token, Rules());

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedWithAnotherKey()
    {
        var token = Generator().GenerateToken(Ada());

        var result = await ValidateAsync(token, Rules(key: "a-completely-different-signing-key-9876543210"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedForAnotherAudienceOrIssuer()
    {
        var token = Generator().GenerateToken(Ada());

        Assert.False((await ValidateAsync(token, Rules(audience: "someone-else"))).IsValid);
        Assert.False((await ValidateAsync(token, Rules(issuer: "someone-else"))).IsValid);
    }

    [Fact]
    public async Task GenerateToken_ProducesATokenThatIsRejectedWhenItsPayloadIsTamperedWith()
    {
        var token = Generator().GenerateToken(Ada());
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

        var first = new JsonWebToken(generator.GenerateToken(user));
        var second = new JsonWebToken(generator.GenerateToken(user));

        Assert.NotEqual(first.Id, second.Id, StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_AndGenerateToken_RejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new JwtTokenGenerator(null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new JwtTokenGenerator(Options.Create(Settings()), null!));
        Assert.Throws<ArgumentNullException>(() => Generator().GenerateToken(null!));
    }
}
