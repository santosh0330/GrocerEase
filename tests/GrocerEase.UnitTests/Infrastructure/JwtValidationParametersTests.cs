using System.Text;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.UnitTests.Infrastructure;

/// <summary>
/// What the API accepts as a valid token. The rules are built from the same settings that sign tokens, so the generator
/// and the validator cannot disagree, and each rule is tested by making a token that breaks only that rule.
/// </summary>
public class JwtValidationParametersTests
{
    private const string Key = "unit-test-signing-key-0123456789-abcdef";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly JwtSettings Settings = new()
    {
        SecretKey = Key,
        Issuer = "issuer-x",
        Audience = "audience-x",
        ExpiryMinutes = 60,
    };

    private static string TokenIssuedAt(DateTimeOffset now) =>
        new JwtTokenGenerator(Options.Create(Settings), new FixedTimeProvider(now))
            .GenerateToken(
                new User(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", "hash", RoleIds.Customer),
                new Role(RoleIds.Customer, RoleNames.Customer));

    private static async Task<TokenValidationResult> ValidateAsync(string token) =>
        await new JsonWebTokenHandler().ValidateTokenAsync(token, JwtValidationParameters.From(Settings));

    private static string Sign(SecurityTokenDescriptor descriptor, string? key = null, string algorithm = SecurityAlgorithms.HmacSha256)
    {
        descriptor.Issuer ??= Settings.Issuer;
        descriptor.Audience ??= Settings.Audience;
        descriptor.Expires ??= DateTime.UtcNow.AddMinutes(5);
        if (key is not null)
        {
            descriptor.SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), algorithm);
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    [Fact]
    public async Task ATokenFromTheGenerator_IsAccepted()
    {
        var result = await ValidateAsync(TokenIssuedAt(DateTimeOffset.UtcNow));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task TheRoleClaimOfAValidToken_IsWhatIsInRoleChecks()
    {
        var result = await ValidateAsync(TokenIssuedAt(DateTimeOffset.UtcNow));

        var principal = new System.Security.Claims.ClaimsPrincipal(result.ClaimsIdentity);
        Assert.True(principal.IsInRole(RoleNames.Customer));
        Assert.False(principal.IsInRole(RoleNames.Admin));
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected() =>
        Assert.False((await ValidateAsync(Sign(new SecurityTokenDescriptor(), key: "another-signing-key-0123456789-abcdefghij"))).IsValid);

    [Fact]
    public async Task ATokenForAnotherAudience_IsRejected() =>
        Assert.False((await ValidateAsync(Sign(new SecurityTokenDescriptor { Audience = "someone-else" }, Key))).IsValid);

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRejected() =>
        Assert.False((await ValidateAsync(Sign(new SecurityTokenDescriptor { Issuer = "someone-else" }, Key))).IsValid);

    [Fact]
    public async Task AnUnsignedToken_IsRejected()
    {
        // Algorithm "none": the classic trick of a token that claims to need no signature.
        var result = await ValidateAsync(Sign(new SecurityTokenDescriptor()));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ATokenSignedWithADifferentAlgorithmEvenWithTheRightKey_IsRejected()
    {
        // HS384 needs a key longer than 384 bits, so this test uses a longer key. Without the algorithm restriction the very
        // same token is accepted (I checked), so the restriction is what stops it.
        var longKey = new string('k', 64);
        var settings = new JwtSettings { SecretKey = longKey, Issuer = Settings.Issuer, Audience = Settings.Audience, ExpiryMinutes = 60 };
        var hs384Token = Sign(new SecurityTokenDescriptor(), longKey, SecurityAlgorithms.HmacSha384);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(hs384Token, JwtValidationParameters.From(settings));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ATokenWithNoExpiry_IsRejected()
    {
        // The handler adds a default expiry when a descriptor has none, so write the payload by hand to leave it out.
        var token = new JsonWebTokenHandler().CreateToken(
            $$"""{"iss":"{{Settings.Issuer}}","aud":"{{Settings.Audience}}","sub":"x"}""",
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenNoExpirationException>(result.Exception);
    }

    [Fact]
    public async Task ATokenThatExpiredAMinuteAgo_IsRejected_AndSaysItExpired()
    {
        var issuedAMinuteAndAHourAgo = DateTimeOffset.UtcNow.AddMinutes(-61);

        var result = await ValidateAsync(TokenIssuedAt(issuedAMinuteAndAHourAgo));

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task ATokenThatExpiredTenSecondsAgo_IsStillAccepted_WithinTheClockSkew()
    {
        // The skew allows for two servers whose clocks differ a little. It is deliberately small: the default is five minutes.
        var result = await ValidateAsync(TokenIssuedAt(DateTimeOffset.UtcNow.AddMinutes(-60).AddSeconds(-10)));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public void TheClockSkew_IsThirtySeconds_NotTheFiveMinuteDefault() =>
        Assert.Equal(TimeSpan.FromSeconds(30), JwtValidationParameters.From(Settings).ClockSkew);

    [Fact]
    public async Task ATokenThatIsNotValidYet_IsRejected()
    {
        var issuedAnHourFromNow = DateTimeOffset.UtcNow.AddHours(1);

        Assert.False((await ValidateAsync(TokenIssuedAt(issuedAnHourFromNow))).IsValid);
    }

    [Fact]
    public async Task ATokenWhosePayloadWasEdited_IsRejected()
    {
        var parts = TokenIssuedAt(DateTimeOffset.UtcNow).Split('.');
        var forged = Base64UrlEncoder.Encode($$"""{"sub":"someone-else","iss":"{{Settings.Issuer}}","aud":"{{Settings.Audience}}","exp":9999999999}""");

        Assert.False((await ValidateAsync($"{parts[0]}.{forged}.{parts[2]}")).IsValid);
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => JwtValidationParameters.From(null!));

    [Fact]
    public void TheSigningKey_IsTheOneTheGeneratorSignsWith()
    {
        var key = Settings.CreateSigningKey();

        Assert.Equal(Encoding.UTF8.GetBytes(Key), key.Key);
    }
}
