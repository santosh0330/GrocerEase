using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Api.Logging;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrocerEase.IntegrationTests;

/// <summary>The logs reach Seq as structured events, without Seq ever being able to slow down or stop the API.</summary>
public class SeqSinkTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private WebApplicationFactory<Program> WithSeq(string url, string? apiKey = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Seq:ServerUrl"] = url };
            if (apiKey is not null)
            {
                settings["Seq:ApiKey"] = apiKey;
            }

            config.AddInMemoryCollection(settings);
        }));

    /// <summary>
    /// Runs the requests, then disposes the app: shutting down flushes the sink's last batch, so the test does not have to wait
    /// for the batching period (a couple of seconds) to pass.
    /// </summary>
    private static async Task UseAndShutDownAsync(WebApplicationFactory<Program> app, Func<HttpClient, Task> requests)
    {
        using (app)
        {
            using var client = app.CreateClient();
            await requests(client);
        }
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? email = null, string password = Password) =>
        client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", email ?? $"ada-{Guid.NewGuid():N}@example.com", password));

    [Fact]
    public async Task TheRequestEvent_ArrivesAtSeq_AsAJsonEventWithItsProperties()
    {
        await using var seq = await FakeSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(seq.Url), client => RegisterAsync(client));

        var request = Assert.Single(seq.Events, e => string.Equals(e.GetProperty("@mt").GetString(), RequestLoggingExtensions.MessageTemplate, StringComparison.Ordinal));
        Assert.Equal("POST", request.GetProperty("RequestMethod").GetString());
        Assert.Equal(200, request.GetProperty("StatusCode").GetInt32());
        Assert.Equal("GrocerEase.Api", request.GetProperty("Application").GetString());
        Assert.True(request.GetProperty("Elapsed").GetDouble() > 0);
    }

    [Fact]
    public async Task EachEvent_CarriesItsTimestampItsTemplateAndItsLevel()
    {
        await using var seq = await FakeSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(seq.Url), client => client.GetAsync(new Uri("/no-such-route", UriKind.Relative)));

        var warning = Assert.Single(seq.Events, e => string.Equals(e.GetProperty("@mt").GetString(), RequestLoggingExtensions.MessageTemplate, StringComparison.Ordinal));
        Assert.True(DateTimeOffset.TryParse(warning.GetProperty("@t").GetString(), System.Globalization.CultureInfo.InvariantCulture, out var timestamp));
        Assert.InRange(timestamp, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.Equal("HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms", warning.GetProperty("@mt").GetString());
        Assert.Equal("Warning", warning.GetProperty("@l").GetString());
        Assert.Equal(404, warning.GetProperty("StatusCode").GetInt32());
    }

    [Fact]
    public async Task AnException_ArrivesAtSeq_WithItsStackTrace()
    {
        await using var seq = await FakeSeqServer.StartAsync();
        var app = WithSeq(seq.Url).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, SeqThrowingHandler>()));

        await UseAndShutDownAsync(app, client => client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest("ada@example.com", Password)));

        var crash = Assert.Single(seq.Events, e => e.TryGetProperty("@x", out _));
        Assert.Contains("seq handler broke", crash.GetProperty("@x").GetString(), StringComparison.Ordinal);
        Assert.Contains("   at ", crash.GetProperty("@x").GetString(), StringComparison.Ordinal);
        Assert.Equal("Error", crash.GetProperty("@l").GetString());
    }

    [Fact]
    public async Task TheLastEventsBeforeShutdown_ArePosted_NotLost()
    {
        await using var seq = await FakeSeqServer.StartAsync();
        var app = WithSeq(seq.Url);

        using (app)
        {
            using var client = app.CreateClient();
            // Logged an instant before the host goes away. The sink would post it at its next tick, a couple of seconds from now.
            var logger = app.Services.GetRequiredService<ILogger<SeqSinkTests>>();
            ShutdownLogMessages.LastWords(logger, "shutdown-flush-canary");
        }

        Assert.Contains(seq.Events, e => e.TryGetProperty("Canary", out var canary) && string.Equals(canary.GetString(), "shutdown-flush-canary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheApiKey_IsSentWithEveryPost_WhenOneIsConfigured()
    {
        await using var seq = await FakeSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(seq.Url, apiKey: "seq-api-key-for-tests"), client => RegisterAsync(client));

        Assert.NotEmpty(seq.Posts);
        Assert.All(seq.Posts, post => Assert.Equal("seq-api-key-for-tests", post.ApiKey));
    }

    [Fact]
    public async Task WithoutAnApiKey_NoneIsSent()
    {
        await using var seq = await FakeSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(seq.Url), client => RegisterAsync(client));

        Assert.NotEmpty(seq.Posts);
        Assert.All(seq.Posts, post => Assert.Null(post.ApiKey));
    }

    [Fact]
    public async Task TheApiKey_IsNotInTheLogsItself()
    {
        await using var seq = await FakeSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(seq.Url, apiKey: "seq-api-key-canary-51ee"), client => RegisterAsync(client));

        Assert.DoesNotContain(seq.Posts, post => post.Body.Contains("seq-api-key-canary-51ee", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingTheRequestCarried_IsSentToSeq()
    {
        await using var seq = await FakeSeqServer.StartAsync();
        string token = string.Empty;
        string refreshToken = string.Empty;

        await UseAndShutDownAsync(WithSeq(seq.Url), async client =>
        {
            var registered = (await (await client.PostAsJsonAsync(
                new Uri("/auth/register?secret=seq-query-canary-77", UriKind.Relative),
                new RegisterRequest("Ada", "Lovelace", "seq-canary@example.com", "Password-canary-c41d!"))).Content.ReadFromJsonAsync<AuthenticationResponse>())!;
            token = registered.Token;
            refreshToken = registered.RefreshToken;
        });

        var everything = string.Join('\n', seq.Posts.Select(post => post.Body));
        Assert.NotEmpty(everything);
        foreach (var secret in new[] { "seq-query-canary-77", "Password-canary-c41d!", "seq-canary@example.com", token, refreshToken })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WithAnEmptyUrl_NothingIsSent_AndTheAppWorks()
    {
        await using var seq = await FakeSeqServer.StartAsync(); // running, but not configured

        await UseAndShutDownAsync(WithSeq(string.Empty), async client =>
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client)).StatusCode));

        Assert.Empty(seq.Posts);
    }

    [Fact]
    public async Task WhenSeqCannotBeReached_TheApiStillAnswers_AndIsNotSlowedDown()
    {
        // A port nothing listens on. The sink retries in the background; a request must not wait for it.
        var app = WithSeq("http://127.0.0.1:1");
        using (app)
        {
            using var client = app.CreateClient();
            await RegisterAsync(client); // warm up: the first call JIT-compiles the pipeline

            var watch = Stopwatch.StartNew();
            var responses = new List<HttpResponseMessage>();
            for (var i = 0; i < 20; i++)
            {
                responses.Add(await client.GetAsync(new Uri("/health/live", UriKind.Relative)));
            }

            watch.Stop();
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"20 requests took {watch.Elapsed}: logging to an unreachable Seq must not block them");
        }
    }

    [Fact]
    public async Task WhenSeqAnswersWithAnError_TheApiStillAnswers()
    {
        await using var broken = await BrokenSeqServer.StartAsync();

        await UseAndShutDownAsync(WithSeq(broken.Url), async client =>
            Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client)).StatusCode));
    }

    [Theory]
    [InlineData("seq")]
    [InlineData("ftp://seq:5341")]
    [InlineData("localhost:5341")]
    public void TheAppDoesNotStart_WithASeqUrlThatIsNotHttp(string url)
    {
        using var app = WithSeq(url);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("Seq:ServerUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefault_PointsAtASeqOnTheLocalMachine()
    {
        // appsettings.json: what "dotnet run" uses next to "docker compose up seq". The test factory switches it off for the tests.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        using var document = JsonDocument.Parse(json);

        Assert.Equal("http://localhost:5341", document.RootElement.GetProperty("Seq").GetProperty("ServerUrl").GetString());
    }
}
