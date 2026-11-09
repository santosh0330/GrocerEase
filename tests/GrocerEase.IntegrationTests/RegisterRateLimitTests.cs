using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>Limits mass account creation and probing for registered emails on /auth/register: 3 per window in these tests (10 per hour in production).</summary>
public class RegisterRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 3;

    private static async Task ExhaustAsync(HttpClient client, string ip)
    {
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, ip)).StatusCode);
        }
    }

    [Fact]
    public async Task TheRegistrationsUpToTheLimitSucceed_ThenTheNextOneIs429()
    {
        using var app = factory.WithRateLimits(registerLimit: "3");
        using var client = app.CreateClient();

        await ExhaustAsync(client, "198.51.100.10");
        var blocked = await RegisterAsync(client, "198.51.100.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemJsonWithARetryAfterHeaderOfTheRegisterWindow()
    {
        // A different window from login's, to show each policy reports its own.
        using var app = factory.WithRateLimits(registerLimit: "3", registerWindow: "120", loginWindow: "60");
        using var client = app.CreateClient();
        await ExhaustAsync(client, "198.51.100.11");

        var blocked = await RegisterAsync(client, "198.51.100.11");

        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("RateLimit.TooManyRequests", body.RootElement.GetProperty("code").GetString());
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out var values));
        Assert.Equal(120, int.Parse(Assert.Single(values), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AnotherClientAddress_IsNotAffected()
    {
        using var app = factory.WithRateLimits(registerLimit: "3");
        using var client = app.CreateClient();
        await ExhaustAsync(client, "198.51.100.12");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, "198.51.100.12")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, "198.51.100.13")).StatusCode);
    }

    [Fact]
    public async Task RefusedAttemptsCountToo_SoProbingForRegisteredEmailsIsLimited()
    {
        using var app = factory.WithRateLimits(registerLimit: "3");
        using var client = app.CreateClient();
        var email = UniqueEmail();
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, "198.51.100.14", email)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync(client, "198.51.100.14", email)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync(client, "198.51.100.14", email)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, "198.51.100.14", email)).StatusCode);
    }

    [Fact]
    public async Task InvalidRequestsCountToo()
    {
        using var app = factory.WithRateLimits(registerLimit: "3");
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(client, "198.51.100.15", email: "not-an-email")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, "198.51.100.15", email: "not-an-email")).StatusCode);
    }

    [Fact]
    public async Task LoginAndRegisterHaveSeparateCounters()
    {
        using var app = factory.WithRateLimits(loginLimit: "3", registerLimit: "3");
        using var client = app.CreateClient();

        // Use up register for this address: login must still answer.
        await ExhaustAsync(client, "198.51.100.16");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, "198.51.100.16")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "198.51.100.16")).StatusCode);

        // Use up login for another address: register must still answer.
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await LoginAsync(client, "198.51.100.17");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "198.51.100.17")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, "198.51.100.17")).StatusCode);
    }

    [Theory]
    [InlineData("0", "3600")]
    [InlineData("-1", "3600")]
    [InlineData("100000", "3600")]
    [InlineData("3", "0")]
    [InlineData("3", "3601")]
    public void TheAppDoesNotStart_WithAnInvalidRegisterLimit(string permitLimit, string windowSeconds)
    {
        using var app = factory.WithRateLimits(registerLimit: permitLimit, registerWindow: windowSeconds);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:Register:", exception.Message, StringComparison.Ordinal);
    }
}
