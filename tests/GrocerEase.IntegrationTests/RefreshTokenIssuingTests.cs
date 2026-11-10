using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;

namespace GrocerEase.IntegrationTests;

/// <summary>Registering and logging in hand out a refresh token in the JSON body, and the database keeps only its SHA-256.</summary>
public class RefreshTokenIssuingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private async Task<HttpResponseMessage> RegisterAsync(string email) =>
        await _client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, "Sup3r-s3cret!"));

    private async Task<HttpResponseMessage> LoginAsync(string email) =>
        await _client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, "Sup3r-s3cret!"));

    private static async Task<string> RefreshTokenOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(body);
        return body.RefreshToken;
    }

    private static async Task<string> AdminConnectionAsync() =>
        await PostgresServer.ConnectionStringAsync(PostgresServer.AdminUser, PostgresServer.AdminPassword);

    [Fact]
    public async Task Register_PutsTheRefreshTokenInTheJsonBody_UnderTheNameRefreshToken()
    {
        var response = await RegisterAsync(UniqueEmail());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("refreshToken").GetString();
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token); // 32 random bytes, base64url
        Assert.False(response.Headers.Contains("Set-Cookie")); // the frontend gets it in the body, not in a cookie
    }

    [Fact]
    public async Task Login_PutsTheRefreshTokenInTheJsonBody()
    {
        var email = UniqueEmail();
        await RegisterAsync(email);

        var response = await LoginAsync(email);

        Assert.Matches("^[A-Za-z0-9_-]{43}$", await RefreshTokenOfAsync(response));
    }

    [Fact]
    public async Task EveryLogin_GetsANewRefreshToken()
    {
        var email = UniqueEmail();
        var registered = await RefreshTokenOfAsync(await RegisterAsync(email));

        var first = await RefreshTokenOfAsync(await LoginAsync(email));
        var second = await RefreshTokenOfAsync(await LoginAsync(email));

        Assert.Equal(3, new[] { registered, first, second }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TheDatabaseHoldsTheSha256OfTheToken_AndNotTheToken()
    {
        var token = await RefreshTokenOfAsync(await RegisterAsync(UniqueEmail()));
        var hashHex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var admin = await AdminConnectionAsync();

        var rowsWithThatHash = await PostgresServer.ScalarAsync<long>(admin, $"select count(*) from refresh_tokens where token_hash = decode('{hashHex}', 'hex')");
        var rowsMentioningTheToken = await PostgresServer.ScalarAsync<long>(admin, $"select count(*) from refresh_tokens t where t::text like '%{token}%'");

        Assert.Equal(1L, rowsWithThatHash);
        Assert.Equal(0L, rowsMentioningTheToken);
    }

    [Fact]
    public async Task TheStoredToken_ExpiresInThreeDays_AndItsFamilyInThirty_FromTheSignInTime()
    {
        var before = DateTimeOffset.UtcNow;
        var token = await RefreshTokenOfAsync(await RegisterAsync(UniqueEmail()));
        var after = DateTimeOffset.UtcNow;
        var hashHex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        var lifetimes = await PostgresServer.ScalarAsync<string>(
            await AdminConnectionAsync(),
            $"select to_char(expires_at - created_at, 'DD') || ',' || to_char(family_expires_at - created_at, 'DD') from refresh_tokens where token_hash = decode('{hashHex}', 'hex')");

        Assert.Equal("03,30", lifetimes);
        var created = await PostgresServer.ScalarAsync<DateTime>(
            await AdminConnectionAsync(), $"select created_at at time zone 'utc' from refresh_tokens where token_hash = decode('{hashHex}', 'hex')");
        Assert.InRange(new DateTimeOffset(DateTime.SpecifyKind(created, DateTimeKind.Utc)), before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public async Task ARegistrationThatIsRefused_LeavesNoSecondRefreshTokenBehind()
    {
        // Counted for this one user: other tests share the database and add tokens of their own at the same time.
        var email = UniqueEmail();
        var first = await (await RegisterAsync(email)).Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(first);

        var duplicate = await RegisterAsync(email);

        Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            1L,
            await PostgresServer.ScalarAsync<long>(await AdminConnectionAsync(), $"select count(*) from refresh_tokens where user_id = '{first.Id}'"));
    }
}
