using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GrocerEase.IntegrationTests;

/// <summary>The app must refuse to start with a refresh token lifetime above the 30-day limit, not fail later when a token is issued.</summary>
public class RefreshTokenStartupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private WebApplicationFactory<Program> With(string lifetimeDays, string maxLifetimeDays) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["RefreshToken:LifetimeDays"] = lifetimeDays,
                ["RefreshToken:MaxLifetimeDays"] = maxLifetimeDays,
            })));

    [Theory]
    [InlineData("31", "30", "RefreshToken:LifetimeDays")]
    [InlineData("3", "31", "RefreshToken:MaxLifetimeDays")]
    [InlineData("0", "30", "RefreshToken:LifetimeDays")]
    public void TheAppDoesNotStart_WithARefreshTokenSettingOutsideTheLimit(string lifetimeDays, string maxLifetimeDays, string expected)
    {
        using var app = With(lifetimeDays, maxLifetimeDays);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppStarts_WithTheDefaultsOfThreeAndThirtyDays()
    {
        using var app = With("3", "30");

        using var client = app.CreateClient();

        Assert.NotNull(client);
    }
}
