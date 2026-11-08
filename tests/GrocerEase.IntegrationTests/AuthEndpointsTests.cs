using System.Net;
using System.Net.Http.Json;
using System.Text;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GrocerEase.IntegrationTests;

public class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_Register_ReturnsOkWithAuthenticationResponse()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", "ada@example.com", "Sup3r-s3cret!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(body);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com"), (body.FirstName, body.LastName, body.Email));
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.False(string.IsNullOrEmpty(body.Token));
    }

    [Fact]
    public async Task Post_Login_ReturnsOkWithAuthenticationResponse()
    {
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("ada@example.com", "s3cret!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(body);
        Assert.Equal("ada@example.com", body.Email);
        Assert.False(string.IsNullOrEmpty(body.Token));
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
        var response = await _client.PostAsJsonAsync(
            new Uri("/auth/login", UriKind.Relative),
            new LoginRequest("ada@example.com", "a"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string[] SortedKeys(ValidationProblemDetails problem) =>
        [.. problem.Errors.Keys.Order(StringComparer.Ordinal)];
}
