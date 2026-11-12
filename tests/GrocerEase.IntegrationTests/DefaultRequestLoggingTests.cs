using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// What ASP.NET Core's own logging says about a request, once <c>Microsoft.AspNetCore.Hosting.Diagnostics</c> is let through at
/// <c>Information</c> in <c>appsettings.json</c>. These tests describe the built-in behaviour, including its limits, because those
/// limits are the reason the API later logs through Serilog: two lines per request, flat text, one level whatever the outcome.
/// </summary>
public class DefaultRequestLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string HostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    private async Task<List<(string Category, LogLevel Level, string Message, Exception? Exception)>> LogsOf(Func<HttpClient, Task> act)
    {
        var logs = new CapturingLoggerProvider();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = app.CreateClient();
        await act(client);
        return [.. logs.Entries];
    }

    [Fact]
    public async Task EveryRequest_IsLoggedTwice_AsStartingAndFinished()
    {
        var entries = await LogsOf(client => client.GetAsync(new Uri("/test", UriKind.Relative)));

        var lines = entries.Where(e => string.Equals(e.Category, HostingCategory, StringComparison.Ordinal)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(LogLevel.Information, line.Level));
        Assert.StartsWith("Request starting HTTP/1.1 GET http://localhost/test", lines[0].Message, StringComparison.Ordinal);
        Assert.StartsWith("Request finished HTTP/1.1 GET http://localhost/test - 200", lines[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFinishedLine_SaysTheStatusAndTheTimeAsText_NotAsValuesToFilterOn()
    {
        var entries = await LogsOf(client => client.GetAsync(new Uri("/test", UriKind.Relative)));

        var finished = entries.Single(e => e.Message.StartsWith("Request finished", StringComparison.Ordinal));
        Assert.Matches(@" - 200 - text/plain;\+charset=utf-8 \d+(\.\d+)?ms$", finished.Message);
    }

    [Fact]
    public async Task ARefusedRequest_IsStillLoggedAtInformation_TheBuiltInLineDoesNotSayHowItWent()
    {
        var entries = await LogsOf(client => client.GetAsync(new Uri("/nope", UriKind.Relative)));

        var finished = entries.Single(e => e.Message.StartsWith("Request finished", StringComparison.Ordinal));
        Assert.Contains(" - 404 ", finished.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Information, finished.Level);
    }

    [Fact]
    public async Task TheRestOfTheFrameworksChatter_StaysQuiet()
    {
        var entries = await LogsOf(async client =>
        {
            await client.GetAsync(new Uri("/test", UriKind.Relative));
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        var chatter = entries
            .Where(e => e.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) && e.Level < LogLevel.Warning)
            .Select(e => e.Category)
            .Distinct(StringComparer.Ordinal);
        Assert.Equal([HostingCategory], chatter, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheBuiltInLines_CarryNeitherTheBodyNorTheAuthorizationHeader()
    {
        const string headerSecret = "built-in-header-canary-3c1f";
        const string bodySecret = "built-in-body-canary-9e7a";
        var entries = await LogsOf(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/login", UriKind.Relative))
            {
                Content = new StringContent($$"""{"email":"","password":"{{bodySecret}}"}""", Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", headerSecret);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });

        var written = string.Join('\n', entries.Select(e => $"{e.Category} {e.Message} {e.Exception}"));
        Assert.Contains("Request finished HTTP/1.1 POST http://localhost/auth/login - 400", written, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, written, StringComparison.Ordinal);
        Assert.DoesNotContain(bodySecret, written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBuiltInLine_DoesCarryTheQueryString_WhichIsOneReasonTheApiLaterWritesItsOwnEvent()
    {
        const string secret = "query-string-canary-5b2d";
        var entries = await LogsOf(client => client.GetAsync(new Uri($"/test?token={secret}", UriKind.Relative)));

        Assert.Contains(entries, e => e.Message.Contains(secret, StringComparison.Ordinal));
    }
}
