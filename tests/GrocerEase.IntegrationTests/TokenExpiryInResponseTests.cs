using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// <c>expiresAt</c> in the sign-in response, against the real API: register, login and refresh all say when the access token they hand out
/// stops working, so a client knows when to refresh without decoding the token. It must be the very instant the token carries as <c>exp</c>,
/// and it must follow <c>Jwt:ExpiryMinutes</c>.
/// </summary>
public class TokenExpiryInResponseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) => client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

    private static async Task<AuthenticationResponse> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    // register, then log in, then refresh: the three ways to be handed an access token
    private static async Task<(AuthenticationResponse Registered, AuthenticationResponse LoggedIn, AuthenticationResponse Refreshed)> AllThreeAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var registered = await ReadAsync(await PostAsync(client, "/auth/register", new RegisterRequest("Ada", "Lovelace", email, Password)));
        var loggedIn = await ReadAsync(await PostAsync(client, "/auth/login", new LoginRequest(email, Password)));
        var refreshed = await ReadAsync(await PostAsync(client, "/auth/refresh", new RefreshRequest(loggedIn.RefreshToken)));
        return (registered, loggedIn, refreshed);
    }

    private static DateTimeOffset IssuedAtOf(string token) => new(new JsonWebToken(token).IssuedAt, TimeSpan.Zero); // iat is UTC, whole seconds

    private static long ExpOf(string token) => new JsonWebToken(token).GetPayloadValue<long>(JwtRegisteredClaimNames.Exp);

    [Fact]
    public async Task EveryResponseThatHandsOutAToken_SaysItExpiresAtTheInstantTheTokenCarriesAsExp()
    {
        using var client = factory.CreateClient();

        var (registered, loggedIn, refreshed) = await AllThreeAsync(client);

        Assert.All(new[] { registered, loggedIn, refreshed }, response => Assert.Equal(ExpOf(response.Token), response.ExpiresAt.ToUnixTimeSeconds()));
    }

    [Fact]
    public async Task ExpiresAt_IsTenMinutesAfterTheTokenWasIssued_AsConfiguredInTheFileThatShips()
    {
        using var client = factory.CreateClient();

        var (registered, loggedIn, refreshed) = await AllThreeAsync(client);

        Assert.All(
            new[] { registered, loggedIn, refreshed },
            response => Assert.Equal(TimeSpan.FromMinutes(10), response.ExpiresAt - IssuedAtOf(response.Token)));
    }

    [Fact]
    public async Task ExpiresAt_IsInTheFuture_ByAboutTheLifetime_OnTheRealClock()
    {
        using var client = factory.CreateClient();
        var before = DateTimeOffset.UtcNow;

        var registered = await ReadAsync(await PostAsync(client, "/auth/register", new RegisterRequest("Ada", "Lovelace", UniqueEmail(), Password)));

        Assert.InRange(registered.ExpiresAt, before.AddMinutes(10).AddSeconds(-1), DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task ExpiresAt_MovesWhenTheSettingDoes()
    {
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Jwt:ExpiryMinutes"] = "3" })));
        using var client = app.CreateClient();

        var (registered, loggedIn, refreshed) = await AllThreeAsync(client);

        Assert.All(
            new[] { registered, loggedIn, refreshed },
            response =>
            {
                Assert.Equal(TimeSpan.FromMinutes(3), response.ExpiresAt - IssuedAtOf(response.Token));
                Assert.Equal(ExpOf(response.Token), response.ExpiresAt.ToUnixTimeSeconds());
            });
    }

    [Fact]
    public async Task OnTheWire_ExpiresAtIsAnIso8601InstantInUtc_NamedInCamelCase_AndWholeSeconds()
    {
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "/auth/register", new RegisterRequest("Ada", "Lovelace", UniqueEmail(), Password));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var text = document.RootElement.GetProperty("expiresAt").GetString()!;
        var parsed = DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(Z|\+00:00)$", text);
    }

    [Fact]
    public async Task AFailedSignIn_SaysNothingOfExpiry()
    {
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "/auth/login", new LoginRequest(UniqueEmail(), Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("expiresAt", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
