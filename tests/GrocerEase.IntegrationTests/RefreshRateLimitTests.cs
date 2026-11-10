using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>Limits guessing and replaying on /auth/refresh: 3 per window in these tests (30 per minute in production).</summary>
public class RefreshRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 3;

    [Fact]
    public async Task TheRefreshesUpToTheLimitAreAnswered_ThenTheNextOneIs429()
    {
        using var app = factory.WithRateLimits(refreshLimit: "3");
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(client, "198.51.100.30")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RefreshAsync(client, "198.51.100.30")).StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemJsonWithARetryAfterHeaderOfTheRefreshWindow()
    {
        using var app = factory.WithRateLimits(refreshLimit: "3", refreshWindow: "90");
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await RefreshAsync(client, "198.51.100.31");
        }

        var blocked = await RefreshAsync(client, "198.51.100.31");

        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("RateLimit.TooManyRequests", body.RootElement.GetProperty("code").GetString());
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out var values));
        Assert.Equal(90, int.Parse(Assert.Single(values), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RefreshHasItsOwnCounter_ApartFromLoginAndRegister()
    {
        using var app = factory.WithRateLimits(loginLimit: "3", registerLimit: "3", refreshLimit: "3");
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await RefreshAsync(client, "198.51.100.32");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RefreshAsync(client, "198.51.100.32")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "198.51.100.32")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, "198.51.100.32")).StatusCode);
    }

    [Theory]
    [InlineData("0", "60")]
    [InlineData("1001", "60")]
    [InlineData("3", "0")]
    [InlineData("3", "3601")]
    public void TheAppDoesNotStart_WithAnInvalidRefreshLimit(string permitLimit, string windowSeconds)
    {
        using var app = factory.WithRateLimits(refreshLimit: permitLimit, refreshWindow: windowSeconds);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:Refresh:", exception.Message, StringComparison.Ordinal);
    }
}
