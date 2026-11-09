using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using static GrocerEase.IntegrationTests.AuthProbe;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Who may reach a protected route. Each rejection is a problem+json with a stable code a client can switch on, and a
/// WWW-Authenticate header, as RFC 6750 asks of a bearer-token API.
/// </summary>
public class BearerAuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string ChallengeOf(HttpResponseMessage response) =>
        string.Join(' ', response.Headers.WwwAuthenticate.Select(h => $"{h.Scheme} {h.Parameter}".Trim()));

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var app = factory.WithAuthProbe();
        using var client = app.CreateClient();
        using (request)
        {
            return await client.SendAsync(request);
        }
    }

    [Fact]
    public async Task WithNoToken_TheAnswerIs401_AProblemWithTheRequiredCode_AndABareBearerChallenge()
    {
        var response = await SendAsync(Get("/test-only/authenticated"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Authentication.Required", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal("Bearer", ChallengeOf(response)); // no "error": the caller simply did not say who they are
    }

    [Fact]
    public async Task ARealTokenFromRegister_IsAccepted_AndTheRouteSeesItsSubject()
    {
        using var app = factory.WithAuthProbe();
        using var client = app.CreateClient();
        var registered = await (await RegisterAsync(client, "203.0.113.70")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/test-only/authenticated", registered!.Token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(registered.Id.ToString(), body.RootElement.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task ARealTokenFromLogin_IsAccepted()
    {
        using var app = factory.WithAuthProbe();
        using var client = app.CreateClient();
        var email = UniqueEmail();
        (await RegisterAsync(client, "203.0.113.71", email)).EnsureSuccessStatusCode();
        var login = await (await LoginAsync(client, "203.0.113.72", email, "Sup3r-s3cret!")).Content.ReadFromJsonAsync<AuthenticationResponse>();

        using var request = GetWithToken("/test-only/authenticated", login!.Token);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Token abc")]
    [InlineData("Bearer")]
    public async Task AnAuthorizationHeaderThatIsNotABearerToken_IsTreatedAsNoToken(string header)
    {
        var response = await SendAsync(Get("/test-only/authenticated", header));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.Required", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATokenWithNoSignature_IsRejected_AsInvalid()
    {
        var response = await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(signed: false)));

        await AssertInvalidTokenAsync(response);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task AGarbageToken_IsRejected_AsInvalid(string token)
    {
        var request = Get("/test-only/authenticated", $"Bearer {token}");

        var response = await SendAsync(request);

        if (token.Length == 0)
        {
            // "Bearer " with nothing after it carries no token at all.
            Assert.Equal("Authentication.Required", (await ProblemAsync(response)).GetProperty("code").GetString());
        }
        else
        {
            await AssertInvalidTokenAsync(response);
        }
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRejected_AsInvalid() =>
        await AssertInvalidTokenAsync(await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(key: "another-signing-key-0123456789-abcdefghij"))));

    [Fact]
    public async Task ATokenForAnotherAudience_IsRejected_AsInvalid() =>
        await AssertInvalidTokenAsync(await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(audience: "someone-else"))));

    [Fact]
    public async Task ATokenFromAnotherIssuer_IsRejected_AsInvalid() =>
        await AssertInvalidTokenAsync(await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(issuer: "someone-else"))));

    [Fact]
    public async Task ATokenWhosePayloadWasEdited_IsRejected_AsInvalid()
    {
        var parts = TestTokens.Create().Split('.');
        var forgedPayload = Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(
            $$"""{"sub":"someone-else","iss":"{{TestTokens.Issuer}}","aud":"{{TestTokens.Audience}}","exp":9999999999}""");

        var response = await SendAsync(GetWithToken("/test-only/authenticated", $"{parts[0]}.{forgedPayload}.{parts[2]}"));

        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task AnExpiredToken_IsRejected_AndTheCodeAndChallengeSayItExpired()
    {
        var response = await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(expiresIn: TimeSpan.FromMinutes(-10))));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.TokenExpired", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal("Bearer error=\"invalid_token\", error_description=\"The token expired\"", ChallengeOf(response));
    }

    [Fact]
    public async Task TheRejection_NeverRepeatsTheLibrarysErrorText_ThatCouldHelpAnAttacker()
    {
        var response = await SendAsync(GetWithToken("/test-only/authenticated", TestTokens.Create(key: "another-signing-key-0123456789-abcdefghij")));

        var text = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("IDX", text, StringComparison.Ordinal); // the library's error ids, which name keys and algorithms
        Assert.DoesNotContain("signature", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASignedInUserWithoutThePermission_Gets403_AProblemWithTheForbiddenCode()
    {
        var response = await SendAsync(GetWithToken("/test-only/admin", TestTokens.Create()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
        Assert.Equal("Authorization.Forbidden", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task ASignedInUserWithThePermission_IsLetIn()
    {
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { ["role"] = "admin" });

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(GetWithToken("/test-only/admin", token))).StatusCode);
    }

    [Fact]
    public async Task APermissionRoute_WithNoToken_Is401NotForbidden_BecauseNobodyIsSignedIn()
    {
        var response = await SendAsync(Get("/test-only/admin"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnOpenRoute_NeedsNoToken() =>
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(Get("/test-only/open"))).StatusCode);

    [Fact]
    public async Task TheAuthEndpoints_StayOpen_SoSomeoneCanGetATokenInTheFirstPlace()
    {
        using var app = factory.WithAuthProbe();
        using var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(client, "203.0.113.73")).StatusCode);
    }

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.InvalidToken", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal("Bearer error=\"invalid_token\"", ChallengeOf(response));
    }
}
