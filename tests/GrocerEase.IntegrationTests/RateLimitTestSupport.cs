using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Shared plumbing for the rate limit tests. Each test builds its own app instance, because the limiter's counters
/// live as long as the app, so tests cannot use up each other's attempts.
/// </summary>
internal static class RateLimitTestSupport
{
    public const string ClientIpHeader = "X-Test-Client-Ip";

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

    /// <summary>A new app instance (fresh counters) with the given rate limit settings, and clients that can pick their address.</summary>
    public static WebApplicationFactory<Program> WithRateLimits(
        this ApiFactory factory,
        string? loginLimit = null,
        string? loginWindow = null,
        string? registerLimit = null,
        string? registerWindow = null,
        string? healthLimit = null,
        string? healthWindow = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Add(settings, "RateLimiting:Login:PermitLimit", loginLimit);
            Add(settings, "RateLimiting:Login:WindowSeconds", loginWindow);
            Add(settings, "RateLimiting:Register:PermitLimit", registerLimit);
            Add(settings, "RateLimiting:Register:WindowSeconds", registerWindow);
            Add(settings, "RateLimiting:Health:PermitLimit", healthLimit);
            Add(settings, "RateLimiting:Health:WindowSeconds", healthWindow);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ClientIpStartupFilter>());
        });

    private static void Add(Dictionary<string, string?> settings, string key, string? value)
    {
        if (value is not null)
        {
            settings[key] = value;
        }
    }

    public static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    public static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string ip, string email = "ada@example.com", string password = "wrong-password")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/login", UriKind.Relative))
        {
            Content = JsonContent.Create(new LoginRequest(email, password)),
        };
        request.Headers.Add(ClientIpHeader, ip);
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string ip, string? email = null, string password = "Sup3r-s3cret!")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/register", UriKind.Relative))
        {
            Content = JsonContent.Create(new RegisterRequest("Ada", "Lovelace", email ?? UniqueEmail(), password)),
        };
        request.Headers.Add(ClientIpHeader, ip);
        return await client.SendAsync(request);
    }
}
