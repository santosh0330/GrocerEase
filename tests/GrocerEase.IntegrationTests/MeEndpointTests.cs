using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Users;
using static GrocerEase.IntegrationTests.AuthProbe;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>GET /me: the first protected endpoint. It tells the signed-in user who the API thinks they are.</summary>
public class MeEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.Required", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().ToString());
    }

    [Fact]
    public async Task WithTheTokenFromRegister_ReturnsTheRegisteredUser()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        var registered = await (await RegisterAsync(client, "203.0.113.80", email)).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/me", registered!.Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal((registered.Id, "Ada", "Lovelace", email, "Customer"), (me!.Id, me.FirstName, me.LastName, me.Email, me.Role));
        Assert.Equal(["products.read", "profile.read", "stores.read"], me.Permissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task WithTheTokenFromLogin_ReturnsTheSameUser()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        var registered = await (await RegisterAsync(client, "203.0.113.81", email)).Content.ReadFromJsonAsync<AuthenticationResponse>();
        var login = await (await LoginAsync(client, "203.0.113.82", email, "Sup3r-s3cret!")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/me", login!.Token);
        var me = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.Equal(registered!.Id, me!.Id);
    }

    [Fact]
    public async Task AnAdminsNewToken_ShowsTheAdminRole()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        await RegisterAsync(client, "203.0.113.85", email);
        await TestUsers.MakeAdminAsync(email);
        var login = await (await LoginAsync(client, "203.0.113.86", email, "Sup3r-s3cret!")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/me", login!.Token);
        var me = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<CurrentUserResponse>();

        Assert.Equal("Admin", me!.Role);
        Assert.Equal(["products.read", "profile.read", "roles.read", "stores.manage", "stores.read"], me.Permissions, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ATokenWithNoRole_Is401_BecauseNoCodeOfOursMadeIt()
    {
        using var client = factory.CreateClient();
        var extra = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["given_name"] = "Ada", ["family_name"] = "Lovelace", ["email"] = "ada@example.com",
        };

        using var request = GetWithToken("/me", TestTokens.Create(extraClaims: extra)); // a name and an email, but no role
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.InvalidToken", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheResponse_HasExactlyTheSixFields_AndNeverATokenOrAPasswordHash()
    {
        using var client = factory.CreateClient();
        var registered = await (await RegisterAsync(client, "203.0.113.83")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/me", registered!.Token);
        using var body = JsonDocument.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync());

        Assert.Equal(
            ["email", "firstName", "id", "lastName", "permissions", "role"],
            body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AnExpiredToken_Is401_WithTheExpiredCode()
    {
        using var client = factory.CreateClient();

        using var request = GetWithToken("/me", TestTokens.Create(expiresIn: TimeSpan.FromMinutes(-10)));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.TokenExpired", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_Is401()
    {
        using var client = factory.CreateClient();

        using var request = GetWithToken("/me", TestTokens.Create(key: "another-signing-key-0123456789-abcdefghij"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.InvalidToken", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AValidlySignedTokenThatDoesNotIdentifyAUser_Is401_NotAServerError()
    {
        using var client = factory.CreateClient();

        using var request = GetWithToken("/me", TestTokens.Create()); // has a subject, but no name and no email
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.InvalidToken", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task OtherMethods_AreNotAllowed(string method)
    {
        using var client = factory.CreateClient();
        var registered = await (await RegisterAsync(client, "203.0.113.84")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri("/me", UriKind.Relative))
        {
            Headers = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", registered!.Token) },
        };

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(request)).StatusCode);
    }
}
