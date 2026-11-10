using System.Net;
using System.Security.Claims;
using GrocerEase.Api.Logging;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;

namespace GrocerEase.UnitTests.Api;

public class RequestLoggingTests
{
    /// <summary>Remembers what <c>Set</c> was asked to record, instead of writing it anywhere.</summary>
    private sealed class RecordingDiagnostics : IDiagnosticContext
    {
        public Dictionary<string, object?> Properties { get; } = new(StringComparer.Ordinal);

        public void Set(string propertyName, object? value, bool destructureObjects = false) => Properties[propertyName] = value;

        public void SetException(Exception exception) => throw new NotSupportedException();
    }

    private static DefaultHttpContext Context(string path = "/auth/login", int status = 200)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;
        return context;
    }

    [Theory]
    [InlineData(200, LogEventLevel.Information)]
    [InlineData(204, LogEventLevel.Information)]
    [InlineData(302, LogEventLevel.Information)]
    [InlineData(400, LogEventLevel.Warning)]
    [InlineData(401, LogEventLevel.Warning)]
    [InlineData(404, LogEventLevel.Warning)]
    [InlineData(409, LogEventLevel.Warning)]
    [InlineData(429, LogEventLevel.Warning)]
    [InlineData(500, LogEventLevel.Error)]
    [InlineData(503, LogEventLevel.Error)]
    public void AnOrdinaryRequest_IsLoggedAtALevelThatMatchesItsOutcome(int status, LogEventLevel expected) =>
        Assert.Equal(expected, RequestLoggingExtensions.LevelFor(Context(status: status), 5, null));

    [Fact]
    public void ARequestThatThrew_IsAnError_EvenIfTheStatusLooksFine() =>
        Assert.Equal(LogEventLevel.Error, RequestLoggingExtensions.LevelFor(Context(status: 200), 5, new InvalidOperationException()));

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/HEALTH/live")]
    public void AHealthyProbe_IsDebug_SoAProbeEveryFewSecondsDoesNotBuryTheLog(string path) =>
        Assert.Equal(LogEventLevel.Debug, RequestLoggingExtensions.LevelFor(Context(path, 200), 1, null));

    [Fact]
    public void AnUnhealthyProbe_IsAWarning() =>
        Assert.Equal(LogEventLevel.Warning, RequestLoggingExtensions.LevelFor(Context("/health", 503), 1, null));

    [Fact]
    public void APathThatOnlyStartsLikeHealth_IsNotTreatedAsAProbe() =>
        Assert.Equal(LogEventLevel.Information, RequestLoggingExtensions.LevelFor(Context("/healthy-recipes", 200), 1, null));

    [Fact]
    public void Enrich_RecordsTheRequestAndResponseDetails()
    {
        var context = Context("/auth/register", 200);
        context.Request.Host = new HostString("api.example.com", 8080);
        context.Request.Scheme = "https";
        context.Request.Protocol = "HTTP/2";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = 123;
        context.Request.Headers.UserAgent = "curl/8.0";
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.9");
        context.Items[ResponseSizeMiddleware.ItemKey] = 456L;
        var diagnostics = new RecordingDiagnostics();

        RequestLoggingExtensions.Enrich(diagnostics, context);

        Assert.Equal("api.example.com:8080", diagnostics.Properties["RequestHost"]);
        Assert.Equal("https", diagnostics.Properties["RequestScheme"]);
        Assert.Equal("HTTP/2", diagnostics.Properties["Protocol"]);
        Assert.Equal("curl/8.0", diagnostics.Properties["UserAgent"]);
        Assert.Equal("198.51.100.9", diagnostics.Properties["ClientIp"]);
        Assert.Equal("application/json", diagnostics.Properties["RequestContentType"]);
        Assert.Equal(123L, diagnostics.Properties["RequestContentLength"]);
        Assert.Equal("application/json; charset=utf-8", diagnostics.Properties["ResponseContentType"]);
        Assert.Equal(456L, diagnostics.Properties["ResponseBytes"]);
    }

    [Fact]
    public void Enrich_RecordsTheUserIdOfASignedInUser_AndNothingForAnAnonymousOne()
    {
        var anonymous = new RecordingDiagnostics();
        RequestLoggingExtensions.Enrich(anonymous, Context());

        var signedIn = Context();
        signedIn.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-42"), new Claim("email", "ada@example.com")], "Bearer"));
        var known = new RecordingDiagnostics();
        RequestLoggingExtensions.Enrich(known, signedIn);

        Assert.False(anonymous.Properties.ContainsKey("UserId"));
        Assert.Equal("user-42", known.Properties["UserId"]);
        Assert.DoesNotContain("ada@example.com", known.Properties.Values.Select(v => v?.ToString()), StringComparer.Ordinal); // the id, not the person's details
    }

    [Fact]
    public void Enrich_CutsAnOversizedUserAgent_BecauseTheClientChoosesItsLength()
    {
        var context = Context();
        context.Request.Headers.UserAgent = new string('a', 10_000);
        var diagnostics = new RecordingDiagnostics();

        RequestLoggingExtensions.Enrich(diagnostics, context);

        Assert.Equal(256, ((string)diagnostics.Properties["UserAgent"]!).Length);
    }

    [Fact]
    public void Enrich_NeverRecordsTheCredentialsTheRequestCarried()
    {
        var context = Context("/me");
        context.Request.Headers.Authorization = "Bearer authorization-header-canary";
        context.Request.Headers.Cookie = "session=cookie-canary";
        context.Request.QueryString = new QueryString("?token=query-canary");
        var diagnostics = new RecordingDiagnostics();

        RequestLoggingExtensions.Enrich(diagnostics, context);

        var everything = string.Join(' ', diagnostics.Properties.Select(p => $"{p.Key}={p.Value}"));
        foreach (var canary in new[] { "authorization-header-canary", "cookie-canary", "query-canary", "Authorization", "Cookie", "token=" })
        {
            Assert.DoesNotContain(canary, everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Enrich_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RequestLoggingExtensions.Enrich(null!, Context()));
        Assert.Throws<ArgumentNullException>(() => RequestLoggingExtensions.Enrich(new RecordingDiagnostics(), null!));
        Assert.Throws<ArgumentNullException>(() => RequestLoggingExtensions.LevelFor(null!, 1, null));
    }
}
