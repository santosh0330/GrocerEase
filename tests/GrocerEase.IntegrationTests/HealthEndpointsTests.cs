using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// GET /health says whether this instance can serve requests that need the database; GET /health/live says only that the process
/// is up. Both are open to anyone, so neither may say <i>why</i> something is wrong: that goes to the log.
/// </summary>
public class HealthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] CheckNames(JsonElement body) =>
        [.. body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()!)];

    // An app whose database cannot be reached: right host, a port nothing listens on.
    private async Task<WebApplicationFactory<Program>> WithUnreachableDatabaseAsync(CapturingLogSink? logs = null)
    {
        var unreachable = new NpgsqlConnectionStringBuilder(await PostgresServer.ApplicationConnectionStringAsync()) { Port = 1, Timeout = 2 }.ConnectionString;
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["ConnectionStrings:Default"] = unreachable })));
        return logs is null ? app : app.WithCapturedLogs(logs);
    }

    [Fact]
    public async Task Health_WithAWorkingDatabase_Is200_AndSaysHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await JsonOf(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal(["database"], CheckNames(body), StringComparer.Ordinal);
        Assert.Equal("Healthy", body.GetProperty("checks")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_NeedsNoToken()
    {
        using var client = factory.CreateClient(); // no Authorization header at all

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Health_IsNeverCached_SoAProbeAlwaysSeesTheCurrentState()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_Is200_AndRunsNoChecks()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Empty(CheckNames(body));
    }

    [Fact]
    public async Task Health_WhenTheDatabaseCannotBeReached_Is503_AndNamesTheFailingCheck()
    {
        using var app = await WithUnreachableDatabaseAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await JsonOf(response);
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", body.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.Equal("database", body.GetProperty("checks")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Health_WhenTheDatabaseIsNotMigrated_Is503()
    {
        var blankDatabase = await PostgresServer.CreateEmptyDatabaseAsync(); // reachable, but nothing was created in it
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["ConnectionStrings:Default"] = blankDatabase })));
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Live_StaysHealthy_EvenWhenTheDatabaseIsDown_SoAProbeDoesNotRestartTheAppForAnOutageItCannotFix()
    {
        using var app = await WithUnreachableDatabaseAsync();
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnUnhealthyAnswer_NeverRepeatsTheFailure_ItGoesToTheLogInstead()
    {
        var logs = new CapturingLogSink();
        using var app = await WithUnreachableDatabaseAsync(logs);
        using var client = app.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        var text = await response.Content.ReadAsStringAsync();

        // What the caller sees: no host, user, library, or reason.
        foreach (var secret in new[] { "Npgsql", "localhost", "127.0.0.1", PostgresServer.AppUser, "refused", "Exception", "Timeout", "Port" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }

        // What an operator sees: the failure, with the exception, in the log.
        var entry = Assert.Single(logs.Entries, e => e.Category.Contains("HealthChecks", StringComparison.Ordinal) && e.Exception is not null);
        Assert.True(entry.Level >= LogLevel.Warning);
        Assert.Contains("database", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_IsRateLimitedPerClient_BecauseItAsksTheDatabaseOnEveryCall()
    {
        using var app = factory.WithRateLimits(healthLimit: "3", healthWindow: "60");
        using var client = app.CreateClient();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
        }

        var blocked = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        Assert.True(blocked.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Live_IsNotRateLimited_BecauseItTouchesNothing()
    {
        using var app = factory.WithRateLimits(healthLimit: "1", healthWindow: "60");
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode);
        }
    }

    [Theory]
    [InlineData("0", "60")]
    [InlineData("100000", "60")]
    [InlineData("3", "0")]
    [InlineData("3", "3601")]
    public void TheAppDoesNotStart_WithAnInvalidHealthLimit(string permitLimit, string windowSeconds)
    {
        using var app = factory.WithRateLimits(healthLimit: permitLimit, healthWindow: windowSeconds);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:Health:", exception.Message, StringComparison.Ordinal);
    }
}
