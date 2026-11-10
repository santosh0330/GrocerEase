using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>POST /auth/refresh against the real API and a real PostgreSQL. A hand-moved clock stands in for "three days later".</summary>
public class RefreshEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    /// <summary>The API with its clock replaced by one the test controls.</summary>
    private WebApplicationFactory<Program> WithClock(AdjustableTimeProvider clock) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client, string? email = null)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email ?? UniqueEmail(), Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<AuthenticationResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password));
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(refreshToken));

    private static async Task AssertInvalidRefreshTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Authentication.InvalidRefreshToken", problem.Extensions["code"]?.ToString());
    }

    private static string HashHex(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static async Task<string> AdminAsync() =>
        await PostgresServer.ConnectionStringAsync(PostgresServer.AdminUser, PostgresServer.AdminPassword);

    [Fact]
    public async Task Refreshing_ReturnsANewPair_ForTheSameUser()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);

        var response = await RefreshAsync(client, signedIn.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(refreshed);
        Assert.Equal((signedIn.Id, signedIn.Email), (refreshed.Id, refreshed.Email));
        Assert.NotEqual(signedIn.RefreshToken, refreshed.RefreshToken, StringComparer.Ordinal);
        Assert.NotEqual(signedIn.Token, refreshed.Token, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheNewAccessToken_IsAcceptedByAProtectedRoute()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);
        var refreshed = (await (await RefreshAsync(client, signedIn.RefreshToken)).Content.ReadFromJsonAsync<AuthenticationResponse>())!;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/me", UriKind.Relative));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", refreshed.Token);
        var me = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(signedIn.Id, (await me.Content.ReadFromJsonAsync<CurrentUserResponse>())!.Id);
    }

    [Fact]
    public async Task TheNewRefreshToken_CanBeRefreshedAgain_AndSoOnDownTheChain()
    {
        using var client = factory.CreateClient();
        var token = (await RegisterAsync(client)).RefreshToken;

        for (var i = 0; i < 5; i++)
        {
            var response = await RefreshAsync(client, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            token = (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken;
        }
    }

    [Fact]
    public async Task Refreshing_NeedsNoAccessToken_BecauseItIsUsuallyExpiredByThen()
    {
        using var client = factory.CreateClient(); // no Authorization header at all
        var signedIn = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, signedIn.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task AUsedRefreshToken_IsRefused()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);
        await RefreshAsync(client, signedIn.RefreshToken);

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, signedIn.RefreshToken));
    }

    [Fact]
    public async Task ReusingAnOldToken_RevokesTheWholeSession_SoTheNewestTokenStopsWorkingToo()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);
        var newest = (await (await RefreshAsync(client, signedIn.RefreshToken)).Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken;

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, signedIn.RefreshToken)); // the thief replays the old one

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, newest)); // so the owner's newest token is dead as well
        Assert.Equal(
            0L,
            await PostgresServer.ScalarAsync<long>(
                await AdminAsync(),
                $"select count(*) from refresh_tokens where family_id = (select family_id from refresh_tokens where token_hash = decode('{HashHex(newest)}', 'hex')) and revoked_at is null"));
    }

    [Fact]
    public async Task ReusingAnOldToken_LeavesTheUsersOtherSessionsAlone()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail();
        var phone = await RegisterAsync(client, email);
        var laptop = await LoginAsync(client, email);
        await RefreshAsync(client, phone.RefreshToken);

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, phone.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, laptop.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownToken_Gets401Problem()
    {
        using var client = factory.CreateClient();

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, "a-token-the-server-never-issued"));
    }

    [Fact]
    public async Task TheAccessTokenIsNotARefreshToken()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, signedIn.Token));
    }

    [Fact]
    public async Task AnEmptyToken_Gets400ValidationProblemKeyedByJsonName()
    {
        using var client = factory.CreateClient();

        var response = await RefreshAsync(client, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["refreshToken"], problem.Errors.Keys.ToArray(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ATokenLongerThanTheLimit_Gets400_WithoutBeingHashed()
    {
        using var client = factory.CreateClient();

        var response = await RefreshAsync(client, new string('a', 10_000));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ABodyWithoutTheField_Gets400()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedJson_Gets400()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content)).StatusCode);
    }

    [Fact]
    public async Task Get_IsMethodNotAllowed_SoATokenCannotBeSentInAUrl()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync(new Uri("/auth/refresh", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task ATokenIsAcceptedUpToThreeDays_AndRefusedFromThen()
    {
        var clock = new AdjustableTimeProvider();
        using var app = WithClock(clock);
        using var client = app.CreateClient();
        var justInTime = await RegisterAsync(client);
        var tooLate = await RegisterAsync(client);

        clock.Advance(TimeSpan.FromDays(3) - TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(client, justInTime.RefreshToken)).StatusCode);

        clock.Advance(TimeSpan.FromSeconds(1)); // exactly three days after sign-in
        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, tooLate.RefreshToken));
    }

    [Fact]
    public async Task AnExpiredToken_DoesNotRevokeTheSession_BecauseExpiryIsNotTheft()
    {
        var clock = new AdjustableTimeProvider();
        using var app = WithClock(clock);
        using var client = app.CreateClient();
        var signedIn = await RegisterAsync(client);
        clock.Advance(TimeSpan.FromDays(4));

        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, signedIn.RefreshToken));

        Assert.Equal(
            0L,
            await PostgresServer.ScalarAsync<long>(
                await AdminAsync(), $"select count(*) from refresh_tokens where token_hash = decode('{HashHex(signedIn.RefreshToken)}', 'hex') and revoked_at is not null"));
    }

    [Fact]
    public async Task EachRefresh_StartsANewThreeDayWindow()
    {
        var clock = new AdjustableTimeProvider();
        using var app = WithClock(clock);
        using var client = app.CreateClient();
        var token = (await RegisterAsync(client)).RefreshToken;

        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromDays(2)); // each time well inside the window of the previous token, but past the first's end
            var response = await RefreshAsync(client, token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            token = (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken;
        }
    }

    [Fact]
    public async Task ASessionRefreshedEveryTwoDays_IsCutOffOnTheThirtiethDay_AndNoTokenEverLivesPastIt()
    {
        var clock = new AdjustableTimeProvider();
        using var app = WithClock(clock);
        using var client = app.CreateClient();
        var signedInAt = clock.GetUtcNow();
        var signedIn = await RegisterAsync(client);
        var token = signedIn.RefreshToken;

        for (var day = 2; day <= 28; day += 2)
        {
            clock.Advance(TimeSpan.FromDays(2));
            var response = await RefreshAsync(client, token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"day {day} should still refresh");
            token = (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.RefreshToken;
        }

        // The family's longest-lived token ends on day 30 exactly, not day 31.
        var longest = await PostgresServer.ScalarAsync<decimal>(
            await AdminAsync(),
            $"select extract(epoch from max(t.expires_at) - min(t.created_at)) / 86400 from refresh_tokens t where t.family_id = (select family_id from refresh_tokens where token_hash = decode('{HashHex(signedIn.RefreshToken)}', 'hex'))");
        Assert.Equal(30m, decimal.Round(longest, 3));

        clock.Advance(TimeSpan.FromDays(2)); // day 30
        await AssertInvalidRefreshTokenAsync(await RefreshAsync(client, token));
        Assert.Equal(signedInAt + TimeSpan.FromDays(30), clock.GetUtcNow());
    }

    [Fact]
    public async Task Refreshing_MarksTheOldRowConsumed_AndStoresOnlyTheHashOfTheNewToken()
    {
        using var client = factory.CreateClient();
        var signedIn = await RegisterAsync(client);

        var refreshed = (await (await RefreshAsync(client, signedIn.RefreshToken)).Content.ReadFromJsonAsync<AuthenticationResponse>())!;

        var admin = await AdminAsync();
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(admin, $"select count(*) from refresh_tokens where token_hash = decode('{HashHex(signedIn.RefreshToken)}', 'hex') and consumed_at is not null"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(admin, $"select count(*) from refresh_tokens where token_hash = decode('{HashHex(refreshed.RefreshToken)}', 'hex') and consumed_at is null"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(admin, $"select count(*) from refresh_tokens t where t::text like '%{refreshed.RefreshToken}%'"));
    }
}
