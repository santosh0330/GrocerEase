using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>Brute-force protection on /auth/login: 3 attempts per window in these tests (5 in production).</summary>
public class LoginRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 3;

    private static async Task ExhaustAsync(HttpClient client, string ip)
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await LoginAsync(client, ip)).StatusCode);
        }
    }

    [Fact]
    public async Task TheAttemptsUpToTheLimitAreAnswered_ThenTheNextOneIs429()
    {
        using var app = factory.WithRateLimits(loginLimit: "3");
        using var client = app.CreateClient();

        await ExhaustAsync(client, "203.0.113.10");
        var blocked = await LoginAsync(client, "203.0.113.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemJsonWithARetryAfterHeaderOfTheLoginWindow()
    {
        using var app = factory.WithRateLimits(loginLimit: "3", loginWindow: "60");
        using var client = app.CreateClient();
        await ExhaustAsync(client, "203.0.113.11");

        var blocked = await LoginAsync(client, "203.0.113.11");

        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("RateLimit.TooManyRequests", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("detail").GetString()));
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));

        Assert.True(blocked.Headers.TryGetValues("Retry-After", out var values));
        Assert.Equal(60, int.Parse(Assert.Single(values), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AnotherClientAddress_IsNotAffectedByTheFirstOnesAttempts()
    {
        using var app = factory.WithRateLimits(loginLimit: "3");
        using var client = app.CreateClient();
        await ExhaustAsync(client, "203.0.113.12");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.12")).StatusCode);

        var other = await LoginAsync(client, "203.0.113.13");

        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task EveryAttemptCounts_EvenOnesTheValidatorRejects()
    {
        using var app = factory.WithRateLimits(loginLimit: "3");
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await LoginAsync(client, "203.0.113.14", email: "", password: "")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.14", email: "", password: "")).StatusCode);
    }

    [Fact]
    public async Task SuccessfulLoginsCountToo()
    {
        using var app = factory.WithRateLimits(loginLimit: "3");
        using var client = app.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, "203.0.113.15", email)).EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, "203.0.113.15", email, "Sup3r-s3cret!")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.15", email, "Sup3r-s3cret!")).StatusCode);
    }

    [Theory]
    [InlineData("0", "60")]
    [InlineData("-1", "60")]
    [InlineData("100000", "60")]
    [InlineData("3", "0")]
    [InlineData("3", "3601")]
    public void TheAppDoesNotStart_WithAnInvalidLoginLimit(string permitLimit, string windowSeconds)
    {
        using var app = factory.WithRateLimits(loginLimit: permitLimit, loginWindow: windowSeconds);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:Login:", exception.Message, StringComparison.Ordinal);
    }
}
