using System.ComponentModel.DataAnnotations;
using GrocerEase.Infrastructure.Authentication;

namespace GrocerEase.UnitTests.Infrastructure;

public class JwtSettingsTests
{
    private static JwtSettings Settings(
        string? key = null, string issuer = "GrocerEase", string audience = "GrocerEase", int expiryMinutes = 60) => new()
    {
        SecretKey = key ?? new string('k', JwtSettings.MinimumSecretKeyLength),
        Issuer = issuer,
        Audience = audience,
        ExpiryMinutes = expiryMinutes,
    };

    private static List<ValidationResult> Validate(JwtSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void AKeyOfExactlyTheMinimumLength_IsAccepted() => Assert.Empty(Validate(Settings()));

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void ATooShortOrMissingKey_IsRejected_AndTheMessageNamesTheSetting(string key)
    {
        var error = Assert.Single(Validate(Settings(key: key)));

        Assert.Contains("Jwt:SecretKey", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyOneCharacterShort_IsRejected() =>
        Assert.Single(Validate(Settings(key: new string('k', JwtSettings.MinimumSecretKeyLength - 1))));

    [Fact]
    public void ABlankIssuer_IsRejected() => Assert.Single(Validate(Settings(issuer: "")));

    [Fact]
    public void ABlankAudience_IsRejected() => Assert.Single(Validate(Settings(audience: "")));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void AnExpiryOutsideOneMinuteToOneDay_IsRejected(int minutes) => Assert.Single(Validate(Settings(expiryMinutes: minutes)));

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public void TheEdgesOfTheExpiryRange_AreAccepted(int minutes) => Assert.Empty(Validate(Settings(expiryMinutes: minutes)));
}
