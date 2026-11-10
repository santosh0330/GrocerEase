using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GrocerEase.Api.Logging;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>One structured event per request, with the request and the response on it, at a level that matches the outcome.</summary>
public class RequestLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private sealed class ThrowingHandler : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
    {
        public Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the handler broke");
    }

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static IEnumerable<LogEvent> RequestEvents(CapturingLogSink logs) =>
        logs.Events.Where(e => string.Equals(e.MessageTemplate.Text, RequestLoggingExtensions.MessageTemplate, StringComparison.Ordinal));

    private static string Text(LogEvent logEvent, string property) => CapturingLogSink.Text(logEvent, property)!;

    /// <summary>Runs one request against a fresh app that records its logs; returns the response and the one request event it produced.</summary>
    private async Task<(HttpResponseMessage Response, LogEvent Event, CapturingLogSink Logs)> SendAsync(Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();
        var response = await send(client);
        return (response, Assert.Single(RequestEvents(logs)), logs);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? email = null) =>
        client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email ?? UniqueEmail(), Password));

    [Fact]
    public async Task ARequest_IsLoggedOnce_WithItsMethodItsPathAndTheStatusSent()
    {
        var before = DateTimeOffset.UtcNow;

        var (_, logged, _) = await SendAsync(client => RegisterAsync(client));

        Assert.Equal("POST", Text(logged, "RequestMethod"));
        Assert.Equal("/auth/register", Text(logged, "RequestPath"));
        Assert.Equal("200", Text(logged, "StatusCode"));
        Assert.Equal(LogEventLevel.Information, logged.Level);
        Assert.StartsWith("HTTP \"POST\" \"/auth/register\" responded 200 in ", logged.RenderMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.InRange(logged.Timestamp, before.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task TheEvent_SaysHowLongTheRequestTook_InMilliseconds()
    {
        var (_, logged, _) = await SendAsync(client => RegisterAsync(client)); // registering hashes a password: not instant

        var elapsed = double.Parse(Text(logged, "Elapsed"), CultureInfo.InvariantCulture);

        Assert.InRange(elapsed, 1, 60_000);
    }

    [Fact]
    public async Task TheEvent_NamesTheEndpointThatHandledIt()
    {
        var (_, logged, _) = await SendAsync(client => RegisterAsync(client));

        Assert.Contains("AuthController.Register", Text(logged, "Endpoint"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEvent_RecordsTheRequestDetails_HostSchemeProtocolUserAgentAndBody()
    {
        var body = JsonContent.Create(new RegisterRequest("Ada", "Lovelace", UniqueEmail(), Password));
        var length = (await body.ReadAsStringAsync()).Length;

        var (_, logged, _) = await SendAsync(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/register", UriKind.Relative)) { Content = body };
            request.Headers.UserAgent.ParseAdd("request-logging-tests/1.0");
            return await client.SendAsync(request);
        });

        Assert.Equal("localhost", Text(logged, "RequestHost"));
        Assert.Equal("http", Text(logged, "RequestScheme"));
        Assert.Equal("HTTP/1.1", Text(logged, "Protocol"));
        Assert.Equal("request-logging-tests/1.0", Text(logged, "UserAgent"));
        Assert.StartsWith("application/json", Text(logged, "RequestContentType"), StringComparison.Ordinal);
        Assert.Equal(length.ToString(CultureInfo.InvariantCulture), Text(logged, "RequestContentLength"));
    }

    [Fact]
    public async Task TheEvent_RecordsTheResponseDetails_ContentTypeAndTheBytesSent()
    {
        var (response, logged, _) = await SendAsync(client => RegisterAsync(client));
        var received = (await response.Content.ReadAsByteArrayAsync()).Length;

        Assert.StartsWith("application/json", Text(logged, "ResponseContentType"), StringComparison.Ordinal);
        Assert.Equal(received.ToString(CultureInfo.InvariantCulture), Text(logged, "ResponseBytes"));
    }

    [Fact]
    public async Task TheClientAddress_IsRecorded_WhenTheServerKnowsIt()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithRateLimits().WithCapturedLogs(logs); // the same test hook the rate limit tests use to choose an address
        using var client = app.CreateClient();

        await RateLimitTestSupport.RegisterAsync(client, "198.51.100.77");

        Assert.Equal("198.51.100.77", Text(Assert.Single(RequestEvents(logs)), "ClientIp"));
    }

    [Fact]
    public async Task ASignedInRequest_IsLoggedWithTheUserId_NotTheToken()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();
        var registered = (await (await RegisterAsync(client)).Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/me", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registered.Token);

        await client.SendAsync(request);

        var me = Assert.Single(RequestEvents(logs), e => string.Equals(Text(e, "RequestPath"), "/me", StringComparison.Ordinal));
        Assert.Equal(registered.Id.ToString(), Text(me, "UserId"));
        Assert.DoesNotContain(logs.Texts(), text => text.Contains(registered.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnonymousRequest_HasNoUserId()
    {
        var (_, logged, _) = await SendAsync(client => RegisterAsync(client));

        Assert.False(logged.Properties.ContainsKey("UserId"));
    }

    [Theory]
    [InlineData("/auth/login", 401, LogEventLevel.Warning)] // wrong credentials
    [InlineData("/me", 401, LogEventLevel.Warning)] // no token
    [InlineData("/no-such-route", 404, LogEventLevel.Warning)]
    public async Task ARefusedRequest_IsAWarning_WithTheStatusSent(string path, int status, LogEventLevel level)
    {
        var (response, logged, _) = await SendAsync(client => string.Equals(path, "/auth/login", StringComparison.Ordinal)
            ? client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new LoginRequest("nobody@example.com", Password))
            : client.GetAsync(new Uri(path, UriKind.Relative)));

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(status.ToString(CultureInfo.InvariantCulture), Text(logged, "StatusCode"));
        Assert.Equal(level, logged.Level);
    }

    [Fact]
    public async Task InvalidInput_IsAWarning_With400()
    {
        var (_, logged, _) = await SendAsync(client =>
            client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("", "", "", "")));

        Assert.Equal("400", Text(logged, "StatusCode"));
        Assert.Equal(LogEventLevel.Warning, logged.Level);
    }

    [Fact]
    public async Task ARateLimitedRequest_IsLoggedToo_As429()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithRateLimits(loginLimit: "1").WithCapturedLogs(logs);
        using var client = app.CreateClient();
        await LoginAsync(client, "198.51.100.80");

        await LoginAsync(client, "198.51.100.80");

        var limited = Assert.Single(RequestEvents(logs), e => string.Equals(Text(e, "StatusCode"), "429", StringComparison.Ordinal));
        Assert.Equal(LogEventLevel.Warning, limited.Level);
    }

    [Fact]
    public async Task AHealthyProbe_IsLoggedAtDebug_NotAtInformation()
    {
        var (_, logged, _) = await SendAsync(client => client.GetAsync(new Uri("/health/live", UriKind.Relative)));

        Assert.Equal(LogEventLevel.Debug, logged.Level);
    }

    [Fact]
    public async Task AnUnhandledException_IsLoggedTwice_AnErrorForTheRequest_AndTheExceptionItself_TiedTogether()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, ThrowingHandler>()));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest("ada@example.com", Password));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var request = Assert.Single(RequestEvents(logs));
        Assert.Equal(LogEventLevel.Error, request.Level);
        Assert.Equal("500", Text(request, "StatusCode"));
        var crash = Assert.Single(logs.Events, e => e.Exception is InvalidOperationException);
        Assert.Equal(LogEventLevel.Error, crash.Level);
        Assert.Equal(Text(crash, "RequestId"), Text(request, "RequestId")); // the same request
        Assert.Equal(Text(crash, "TraceId"), Text(request, "TraceId"));
    }

    [Fact]
    public async Task TheClientSeesTheTraceId_ThatTheLogsCarry()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, ThrowingHandler>()));
        using var client = app.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest("ada@example.com", Password));
        var shown = (await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>())!["traceId"].GetString()!;

        // The problem response says traceId "00-<trace>-<span>-00"; the log says TraceId "<trace>". A support request quoting the first finds the second.
        Assert.Contains(Text(Assert.Single(RequestEvents(logs)), "TraceId"), shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryRequest_GetsExactlyOneEvent()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        await client.GetAsync(new Uri("/me", UriKind.Relative));
        await client.GetAsync(new Uri("/no-such-route", UriKind.Relative));

        Assert.Equal(3, RequestEvents(logs).Count());
    }

    [Fact]
    public async Task NothingTheRequestOrTheResponseCarried_ReachesTheLog()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();
        var email = UniqueEmail();
        const string passwordCanary = "Password-canary-4f1e!";
        const string queryCanary = "query-canary-91bd";
        const string headerCanary = "header-canary-77aa";

        using var register = new HttpRequestMessage(HttpMethod.Post, new Uri($"/auth/register?secret={queryCanary}", UriKind.Relative))
        {
            Content = JsonContent.Create(new RegisterRequest("Ada", "Lovelace", email, passwordCanary)),
        };
        register.Headers.TryAddWithoutValidation("X-Api-Secret", headerCanary);
        register.Headers.Authorization = new AuthenticationHeaderValue("Bearer", headerCanary);
        var registered = (await (await client.SendAsync(register)).Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken));

        var written = logs.Texts().ToList();
        foreach (var secret in new[] { passwordCanary, queryCanary, headerCanary, registered.Token, registered.RefreshToken })
        {
            Assert.DoesNotContain(written, text => text.Contains(secret, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(written, text => text.Contains(email, StringComparison.Ordinal)); // the body's contents are not copied either
    }

    [Fact]
    public async Task TheRequestPath_IsLoggedWithoutTheQueryString()
    {
        var (_, logged, _) = await SendAsync(client => client.GetAsync(new Uri("/health/live?x=1&y=2", UriKind.Relative)));

        Assert.Equal("/health/live", Text(logged, "RequestPath"));
    }

    [Fact]
    public async Task AnOversizedUserAgent_IsCut()
    {
        var (_, logged, _) = await SendAsync(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
            request.Headers.TryAddWithoutValidation("User-Agent", new string('a', 5000));
            return await client.SendAsync(request);
        });

        Assert.Equal(256, Text(logged, "UserAgent").Length);
    }

    [Fact]
    public async Task ResponseBytes_MatchWhatTheClientReceived_ForEveryKindOfAnswer()
    {
        var logs = new CapturingLogSink();
        using var app = factory.WithCapturedLogs(logs);
        using var client = app.CreateClient();
        var answers = new List<(string Path, int Received)>();
        foreach (var path in new[] { "/health/live", "/me", "/no-such-route", "/openapi/v1.json" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            answers.Add((path, (await response.Content.ReadAsByteArrayAsync()).Length));
        }

        foreach (var (path, received) in answers)
        {
            var logged = Assert.Single(RequestEvents(logs), e => string.Equals(Text(e, "RequestPath"), path, StringComparison.Ordinal));
            Assert.Equal(received.ToString(CultureInfo.InvariantCulture), Text(logged, "ResponseBytes"));
        }

        Assert.Contains(answers, a => a.Received == 0); // an empty 404 body is a real zero, not a missing value
        Assert.Contains(answers, a => a.Received > 1000); // and a large document is counted whole
    }
}
