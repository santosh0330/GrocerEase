using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Brute-force protection on /auth/login. Every test builds its own app instance, because the limiter's counters live
/// as long as the app, so tests cannot use up each other's attempts.
/// </summary>
public class LoginRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 3;
    private const string ClientIpHeader = "X-Test-Client-Ip";

    // The in-memory test server has no remote address, so this lets a test choose which "client" a request comes from.
    private sealed class ClientIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientIpHeader, out var value) && IPAddress.TryParse(value, out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private WebApplicationFactory<Program> Limited(string permitLimit = "3") =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RateLimiting:Login:PermitLimit"] = permitLimit,
                    ["RateLimiting:Login:WindowSeconds"] = "60",
                }));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>());
        });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string ip, string email = "ada@example.com", string password = "wrong-password")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginRequest(email, password)),
        };
        request.Headers.Add(ClientIpHeader, ip);
        return await client.SendAsync(request);
    }

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
        using var app = Limited();
        using var client = app.CreateClient();

        await ExhaustAsync(client, "203.0.113.10");
        var blocked = await LoginAsync(client, "203.0.113.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemJsonWithARetryAfterHeader()
    {
        using var app = Limited();
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
        var seconds = int.Parse(Assert.Single(values), CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, 60);
    }

    [Fact]
    public async Task AnotherClientAddress_IsNotAffectedByTheFirstOnesAttempts()
    {
        using var app = Limited();
        using var client = app.CreateClient();
        await ExhaustAsync(client, "203.0.113.12");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.12")).StatusCode);

        var other = await LoginAsync(client, "203.0.113.13");

        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task EveryAttemptCounts_EvenOnesTheValidatorRejects()
    {
        using var app = Limited();
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
        using var app = Limited();
        using var client = app.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, "Sup3r-s3cret!")))
            .EnsureSuccessStatusCode();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, "203.0.113.15", email, "Sup3r-s3cret!")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.15", email, "Sup3r-s3cret!")).StatusCode);
    }

    [Fact]
    public async Task RegisterIsNotLimited_EvenFromAnAddressThatIsBlockedOnLogin()
    {
        using var app = Limited();
        using var client = app.CreateClient();
        await ExhaustAsync(client, "203.0.113.16");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "203.0.113.16")).StatusCode);

        for (var i = 0; i < Limit + 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/register", UriKind.Relative))
            {
                Content = JsonContent.Create(new RegisterRequest("Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", "Sup3r-s3cret!")),
            };
            request.Headers.Add(ClientIpHeader, "203.0.113.16");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100000")]
    public void TheAppDoesNotStart_WithAnInvalidLimit(string permitLimit)
    {
        using var app = Limited(permitLimit);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:Login:PermitLimit", exception.Message, StringComparison.Ordinal);
    }
}
