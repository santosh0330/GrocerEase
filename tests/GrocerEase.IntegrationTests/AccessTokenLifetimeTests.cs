using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// How long an access token lives, read from the file that ships. <see cref="ApiFactory"/> does not override <c>Jwt:ExpiryMinutes</c>,
/// so the host reads <c>appsettings.json</c>; a test with its own settings object would pass whatever that file said. It matters because
/// a changed role or permission reaches a token only when the next one is made, so the lifetime is how long a change can take.
/// </summary>
public class AccessTokenLifetimeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static TimeSpan Lifetime(string token)
    {
        var jwt = new JsonWebToken(token);
        return jwt.ValidTo - jwt.ValidFrom;
    }

    [Fact]
    public async Task AnAccessToken_FromRegisteringLivesTenMinutes()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", UniqueEmail(), Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(10), Lifetime((await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.Token));
    }

    [Fact]
    public async Task AnAccessToken_FromLoggingInAndFromRefreshing_LivesTenMinutes()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, Password));

        var login = await (await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>();
        var refreshed = await (await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(login!.RefreshToken)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.Equal(TimeSpan.FromMinutes(10), Lifetime(login.Token));
        Assert.Equal(TimeSpan.FromMinutes(10), Lifetime(refreshed!.Token));
    }
}
