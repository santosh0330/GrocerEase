using System.Text.Json;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The OpenAPI document is the published contract. These tests keep it in step with what the endpoints really return,
/// so a client (or a person reading the docs) learns about the failures as well as the success.
/// </summary>
public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<JsonElement> ResponsesOfAsync(string path)
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("responses").Clone();
    }

    private static string[] Statuses(JsonElement responses) =>
        [.. responses.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task Register_DeclaresSuccessValidationFailureDuplicateEmailAndTooManyAttempts() =>
        Assert.Equal(["200", "400", "409", "429"], Statuses(await ResponsesOfAsync("/auth/register")));

    [Fact]
    public async Task Login_DeclaresSuccessValidationFailureInvalidCredentialsAndTooManyAttempts() =>
        Assert.Equal(["200", "400", "401", "429"], Statuses(await ResponsesOfAsync("/auth/login")));

    [Fact]
    public async Task Refresh_DeclaresSuccessValidationFailureInvalidRefreshTokenAndTooManyAttempts() =>
        Assert.Equal(["200", "400", "401", "429"], Statuses(await ResponsesOfAsync("/auth/refresh")));

    [Fact]
    public async Task Me_DeclaresSuccessAndUnauthorized()
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var document = JsonDocument.Parse(json);

        var responses = document.RootElement.GetProperty("paths").GetProperty("/me").GetProperty("get").GetProperty("responses");

        Assert.Equal(["200", "401"], Statuses(responses));
        Assert.True(responses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Fact]
    public async Task Roles_DeclaresSuccessUnauthorizedAndForbidden_AsProblemJson()
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        using var document = JsonDocument.Parse(json);

        var responses = document.RootElement.GetProperty("paths").GetProperty("/roles").GetProperty("get").GetProperty("responses");

        Assert.Equal(["200", "401", "403"], Statuses(responses));
        Assert.True(responses.GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    [Theory]
    [InlineData("/auth/register", "409")]
    [InlineData("/auth/register", "429")]
    [InlineData("/auth/login", "401")]
    [InlineData("/auth/login", "429")]
    [InlineData("/auth/refresh", "401")]
    [InlineData("/auth/refresh", "429")]
    public async Task TheFailureResponses_AreProblemJson(string path, string status)
    {
        var responses = await ResponsesOfAsync(path);

        var content = responses.GetProperty(status).GetProperty("content");

        Assert.True(content.TryGetProperty("application/problem+json", out _), $"{path} {status} should be application/problem+json");
    }
}
