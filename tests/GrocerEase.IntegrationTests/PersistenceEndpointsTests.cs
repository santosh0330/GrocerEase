using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Authentication;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>What storing users in PostgreSQL means for someone using the API.</summary>
public class PersistenceEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AUserRegisteredBeforeARestart_CanLogInAfterIt()
    {
        var email = UniqueEmail();
        AuthenticationResponse registered;
        using (var firstRun = factory.WithRateLimits())
        using (var client = firstRun.CreateClient())
        {
            registered = (await (await RegisterAsync(client, "203.0.113.50", email)).Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        } // the first app instance is gone: with an in-memory store, the user would be gone with it

        using var secondRun = factory.WithRateLimits();
        using var secondClient = secondRun.CreateClient();
        var login = await LoginAsync(secondClient, "203.0.113.51", email, "Sup3r-s3cret!");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(registered.Id, body?.Id);
    }

    [Fact]
    public async Task TheIdOfANewUser_IsAVersion7Uuid()
    {
        using var app = factory.WithRateLimits();
        using var client = app.CreateClient();

        var body = await (await RegisterAsync(client, "203.0.113.52")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(7, body?.Id.Version);
    }

    [Fact]
    public async Task TheEmailIsStoredInLowerCase_AndLoginAcceptsAnyCase()
    {
        using var app = factory.WithRateLimits();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        var mixedCase = email.Replace("ada", "AdA", StringComparison.Ordinal).ToUpperInvariant();

        var registered = await (await RegisterAsync(client, "203.0.113.53", mixedCase)).Content.ReadFromJsonAsync<AuthenticationResponse>();
        var login = await LoginAsync(client, "203.0.113.54", email, "Sup3r-s3cret!");

        Assert.Equal(email, registered?.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task TenSimultaneousRegistrationsOfOneEmail_GiveOneSuccessAndNineConflicts_NeverAServerError()
    {
        using var app = factory.WithRateLimits();
        using var client = app.CreateClient();
        var email = UniqueEmail();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => RegisterAsync(client, $"198.51.100.{100 + i}", email)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }
}
