using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A refresh token is a credential: whoever reads it can be the user. These tests run every path that touches one (success, reuse,
/// unknown, malformed, a crash) with logging turned up to Trace for every category, EF Core and ASP.NET included, and then search
/// everything that was written for the tokens, and for their hashes in every common encoding.
/// </summary>
public class RefreshTokenLogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private sealed class ThrowingRefreshHandler : ICommandHandler<RefreshCommand, Result<AuthenticationResult>>
    {
        // The sloppy thing a developer might write. The command's own ToString must keep the token out of it.
        public Task<Result<AuthenticationResult>> Handle(RefreshCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"could not refresh {command}");
    }

    private static WebApplicationFactory<Program> Logging(ApiFactory factory, CapturingLoggerProvider logs, bool crashingHandler = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(logs);
                logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace); // everything, whatever appsettings.json says
            });
            if (crashingHandler)
            {
                builder.ConfigureTestServices(services =>
                    services.AddScoped<ICommandHandler<RefreshCommand, Result<AuthenticationResult>>, ThrowingRefreshHandler>());
            }
        });

    /// <summary>Everything that was logged, message and exception, as one text per entry.</summary>
    private static List<string> Written(CapturingLoggerProvider logs) =>
        [.. logs.Entries.Select(entry => $"{entry.Category} {entry.Level} {entry.Message} {entry.Exception}")];

    /// <summary>A token as it could show up: as is, and its SHA-256 as hex (either case), base64 and base64url.</summary>
    private static IEnumerable<string> Forms(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        yield return token;
        yield return Convert.ToHexString(hash);
        yield return Convert.ToHexStringLower(hash);
        yield return Convert.ToBase64String(hash);
        yield return System.Buffers.Text.Base64Url.EncodeToString(hash);
    }

    private static void AssertNeverLogged(CapturingLoggerProvider logs, params string[] tokens)
    {
        var written = Written(logs);
        Assert.NotEmpty(written); // the capture works: an empty log would "pass" every search
        foreach (var token in tokens)
        {
            foreach (var form in Forms(token))
            {
                Assert.DoesNotContain(written, text => text.Contains(form, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public async Task NoPathThatTouchesARefreshToken_WritesItOrItsHashToAnyLog()
    {
        var logs = new CapturingLoggerProvider();
        using var app = Logging(factory, logs);
        using var client = app.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        const string canary = "unknown-token-canary-0f7c1b";
        var oversized = "oversized-token-canary-" + new string('x', 600);

        var registered = (await (await client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, Password)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        var loggedIn = (await (await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        var refreshed = (await (await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken)); // reuse: revokes the family
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(canary)); // unknown
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(oversized)); // rejected by validation
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(refreshed.RefreshToken)); // dead with its family

        AssertNeverLogged(
            logs, registered.RefreshToken, loggedIn.RefreshToken, refreshed.RefreshToken, canary, oversized);
    }

    [Fact]
    public async Task TheLogsOfThatRun_DoShowThatTheFamilyWasRevoked_ByIdAndCountOnly()
    {
        var logs = new CapturingLoggerProvider();
        using var app = Logging(factory, logs);
        using var client = app.CreateClient();
        var registered = (await (await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", $"ada-{Guid.NewGuid():N}@example.com", Password)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken));

        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken)); // reuse

        // An operator needs to see that it happened: a warning that names the family. The token stays out of it.
        var entry = Assert.Single(logs.Entries, e => e.Message.StartsWith("Revoked refresh token family", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("(2 tokens)", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACrashWhileRefreshing_LogsTheException_WithoutTheToken()
    {
        var logs = new CapturingLoggerProvider();
        using var app = Logging(factory, logs, crashingHandler: true);
        using var client = app.CreateClient();
        const string token = "token-in-a-crashing-request-canary-77aa";

        var response = await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(token));

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(Written(logs), text => text.Contains("could not refresh RefreshCommand { RefreshToken = *** }", StringComparison.Ordinal));
        AssertNeverLogged(logs, token);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRequestsBodyIsNotLogged_AndTheTokenIsNotInTheUrl()
    {
        var logs = new CapturingLoggerProvider();
        using var app = Logging(factory, logs);
        using var client = app.CreateClient();
        const string token = "token-in-the-body-canary-5d2e";

        await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(token));

        // The "Request starting" line names the method and path: that is all it can say, because the token is not in the path.
        Assert.Contains(Written(logs), text => text.Contains("/auth/refresh", StringComparison.Ordinal));
        AssertNeverLogged(logs, token);
    }
}
