using System.Net;
using System.Net.Http.Json;
using System.Text;
using GrocerEase.Application.Abstractions;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GrocerEase.IntegrationTests;

public class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // The repository lives as long as the factory, which every test in this class shares: give each test its own email.
    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Post_Register_ReturnsOkWithAuthenticationResponse()
    {
        var email = UniqueEmail();

        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", email, "Sup3r-s3cret!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(body);
        Assert.Equal(("Ada", "Lovelace", email), (body.FirstName, body.LastName, body.Email));
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.False(string.IsNullOrEmpty(body.Token));
    }

    [Fact]
    public async Task Post_Register_ReturnsATokenSignedWithTheConfiguredKeyThatNamesTheUser()
    {
        var registered = await RegisterAsync(UniqueEmail(), "Sup3r-s3cret!");

        var result = await ValidateTokenAsync(registered.Token);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(registered.Id.ToString(), ((JsonWebToken)result.SecurityToken).Subject);
    }

    [Fact]
    public async Task Post_Login_ReturnsATokenSignedWithTheConfiguredKeyThatNamesTheUser()
    {
        var email = UniqueEmail();
        var registered = await RegisterAsync(email, "Sup3r-s3cret!");

        var response = await _client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, "Sup3r-s3cret!"));
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();

        Assert.NotNull(body);
        var result = await ValidateTokenAsync(body.Token);
        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(registered.Id.ToString(), ((JsonWebToken)result.SecurityToken).Subject);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsNotAccepted()
    {
        var registered = await RegisterAsync(UniqueEmail(), "Sup3r-s3cret!");
        var rules = TokenRules(ApiFactory.JwtSecretKey + "-but-not-really");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(registered.Token, rules);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Post_Register_StoresAHashOfThePassword_NotThePassword()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, "Sup3r-s3cret!");

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByEmailAsync(email, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.NotEqual("Sup3r-s3cret!", stored.PasswordHash, StringComparer.Ordinal);
        Assert.DoesNotContain("Sup3r-s3cret!", stored.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_Register_WithAnEmailThatIsAlreadyRegistered_Returns409Problem()
    {
        var email = UniqueEmail();
        var request = new RegisterRequest("Ada", "Lovelace", email, "Sup3r-s3cret!");
        var first = await _client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync(new Uri("/auth/register", UriKind.Relative), request with { Email = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("User.DuplicateEmail", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Post_Login_AfterRegistering_ReturnsOkWithTheSameUser()
    {
        var email = UniqueEmail();
        var registered = await RegisterAsync(email, "Sup3r-s3cret!");

        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(email, "Sup3r-s3cret!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(body);
        Assert.Equal(registered.Id, body.Id);
        Assert.Equal(email, body.Email);
        Assert.False(string.IsNullOrEmpty(body.Token));
    }

    [Fact]
    public async Task Post_Login_WithAWrongPassword_Returns401Problem()
    {
        var email = UniqueEmail();
        await RegisterAsync(email, "Sup3r-s3cret!");

        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(email, "not-the-password"));

        await AssertInvalidCredentialsAsync(response);
    }

    [Fact]
    public async Task Post_Login_WithAnEmailThatIsNotRegistered_Returns401Problem()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(UniqueEmail(), "Sup3r-s3cret!"));

        await AssertInvalidCredentialsAsync(response);
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Post_WithMalformedJson_ReturnsBadRequest(string url)
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(new Uri(url, UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    public async Task Get_ReturnsMethodNotAllowed(string url)
    {
        var response = await _client.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Post_Register_WithEmptyFields_ReturnsValidationProblemKeyedByJsonName()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("", "", "", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["email", "firstName", "lastName", "password"], SortedKeys(problem), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_Register_WithShortPasswordAndBadEmail_ReportsOnlyThoseFields()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", "not-an-email", "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["email", "password"], SortedKeys(problem), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_Login_WithEmptyFields_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["email", "password"], SortedKeys(problem), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_Login_WithShortPassword_IsNotRejectedByThePasswordPolicy()
    {
        // The password policy applies when choosing a password, not when logging in: a short one is a wrong
        // password (401 from the handler), not malformed input (400 from the validator).
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest(UniqueEmail(), "a"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<AuthenticationResponse> RegisterAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static TokenValidationParameters TokenRules(string key) => new()
    {
        ValidIssuer = "GrocerEase",
        ValidAudience = "GrocerEase",
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
    };

    private static Task<TokenValidationResult> ValidateTokenAsync(string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, TokenRules(ApiFactory.JwtSecretKey));

    private static async Task AssertInvalidCredentialsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("Authentication.InvalidCredentials", problem.Extensions["code"]?.ToString());
    }

    private static string[] SortedKeys(ValidationProblemDetails problem) =>
        [.. problem.Errors.Keys.Order(StringComparer.Ordinal)];
}
