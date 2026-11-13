using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Stores;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The people and the stores most endpoint tests need, made through the real API: an admin, a store, a store user assigned to it, a
/// customer. Nothing here is a shortcut into the database except making the first admin, which nothing else can.
/// </summary>
internal static class ApiScenario
{
    public const string Password = "Sup3r-s3cret!";

    public static string UniqueEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static HttpRequestMessage Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = Send(method, path, token, body);
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static async Task<AuthenticationResponse> RegisterAsync(HttpClient client, string? email = null)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Asha", "Rai", email ?? UniqueEmail(), Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    public static async Task<string> LoginTokenAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!.Token;
    }

    public static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);
        return await LoginTokenAsync(client, email);
    }

    public static async Task<Guid> CreateStoreAsync(HttpClient client, string adminToken)
    {
        var response = await SendAsync(client, HttpMethod.Post, "/stores", adminToken, new CreateStoreRequest($"Store {Guid.NewGuid():N}", "1 High Street"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoreResponse>())!.Id;
    }

    /// <summary>A customer assigned to the store by the real endpoint, with a token that already says StoreUser.</summary>
    public static async Task<(string Token, Guid UserId, string Email)> StoreUserOfAsync(HttpClient client, string adminToken, Guid storeId)
    {
        var email = UniqueEmail();
        var registered = await RegisterAsync(client, email);
        var assigned = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/members", adminToken, new AssignStoreMemberRequest(email));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var refreshed = await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var token = (await refreshed.Content.ReadFromJsonAsync<AuthenticationResponse>())!.Token;
        return (token, (await assigned.Content.ReadFromJsonAsync<StoreMemberResponse>())!.UserId, email);
    }
}
