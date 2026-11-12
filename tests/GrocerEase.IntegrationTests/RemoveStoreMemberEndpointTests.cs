using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Stores;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// DELETE /stores/{storeId}/members/{userId} against the real API and PostgreSQL: an admin takes a store user out of a store, which
/// makes them a customer again. What is checked afterwards is read from the database and from fresh tokens.
/// </summary>
public class RemoveStoreMemberEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static string UniqueEmail() => $"asha-{Guid.NewGuid():N}@example.com";

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<AuthenticationResponse> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Asha", "Rai", email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<AuthenticationResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private static async Task<string> AdminTokenAsync(HttpClient client)
    {
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        await TestUsers.MakeAdminAsync(email);
        return (await LoginAsync(client, email)).Token;
    }

    private static HttpRequestMessage Send(HttpMethod method, string path, string? token, object? body = null)
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

    private static async Task<Guid> CreateStoreAsync(HttpClient client, string adminToken)
    {
        using var request = Send(HttpMethod.Post, "/stores", adminToken, new CreateStoreRequest($"Store {Guid.NewGuid():N}", "1 High Street"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoreResponse>())!.Id;
    }

    /// <summary>A registered customer, assigned to a new store by the real endpoint. Returns the store, the user and their email.</summary>
    private static async Task<(Guid StoreId, Guid UserId, string Email)> AStoreUserAsync(HttpClient client, string admin)
    {
        var storeId = await CreateStoreAsync(client, admin);
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        using var request = Send(HttpMethod.Post, $"/stores/{storeId}/members", admin, new AssignStoreMemberRequest(email));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (storeId, (await response.Content.ReadFromJsonAsync<StoreMemberResponse>())!.UserId, email);
    }

    private static async Task<HttpResponseMessage> RemoveAsync(HttpClient client, string? token, Guid storeId, Guid userId)
    {
        using var request = Send(HttpMethod.Delete, $"/stores/{storeId}/members/{userId}", token);
        return await client.SendAsync(request);
    }

    private static async Task<(string Role, long Memberships)> StateOfAsync(string email)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            (await PostgresServer.ScalarAsync<string>(connection, $"select r.name from users u join roles r on r.id = u.role_id where u.email = '{email}'"))!,
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from store_members m join users u on u.id = m.user_id where u.email = '{email}'"));
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RemoveAsync(client, token: null, Guid.CreateVersion7(), Guid.CreateVersion7())).StatusCode);
    }

    [Fact]
    public async Task ACustomerAndAStoreUser_Are403_AndTheMemberStaysWhereTheyAre()
    {
        using var client = factory.CreateClient();
        var (storeId, userId, email) = await AStoreUserAsync(client, await AdminTokenAsync(client));
        var customer = await RegisterAsync(client, UniqueEmail());
        var storeUser = await LoginAsync(client, email); // the member themselves

        var asCustomer = await RemoveAsync(client, customer.Token, storeId, userId);
        var asStoreUser = await RemoveAsync(client, storeUser.Token, storeId, userId);

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asStoreUser.StatusCode); // not even to remove themselves
        Assert.Equal((RoleNames.StoreUser, 1L), await StateOfAsync(email));
    }

    [Fact]
    public async Task AnAdmin_TakesAStoreUserOut_WhoIsACustomerAgain()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (storeId, userId, email) = await AStoreUserAsync(client, admin);
        var oldToken = (await LoginAsync(client, email)).Token; // made while they ran the store

        var response = await RemoveAsync(client, admin, storeId, userId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((RoleNames.Customer, 0L), await StateOfAsync(email));
        Assert.Equal(RoleNames.StoreUser, new JsonWebToken(oldToken).GetClaim(JwtClaims.Role).Value); // a token is a copy, until it expires
        Assert.Equal(RoleNames.Customer, new JsonWebToken((await LoginAsync(client, email)).Token).GetClaim(JwtClaims.Role).Value);
    }

    [Fact]
    public async Task APersonTakenOut_CanBeAssignedAgain_ToAnotherStore()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (storeId, userId, email) = await AStoreUserAsync(client, admin);
        await RemoveAsync(client, admin, storeId, userId);
        var another = await CreateStoreAsync(client, admin);

        using var request = Send(HttpMethod.Post, $"/stores/{another}/members", admin, new AssignStoreMemberRequest(email));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((RoleNames.StoreUser, 1L), await StateOfAsync(email));
    }

    [Fact]
    public async Task AnUnknownStore_Is404_WithTheStoreNotFoundCode()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (_, userId, email) = await AStoreUserAsync(client, admin);

        var response = await RemoveAsync(client, admin, Guid.CreateVersion7(), userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal((RoleNames.StoreUser, 1L), await StateOfAsync(email));
    }

    [Fact]
    public async Task AUserWhoIsNotAMemberOfThatStore_Is404_AndAMemberOfAnotherStoreIsUntouched()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (_, userId, email) = await AStoreUserAsync(client, admin);
        var otherStore = await CreateStoreAsync(client, admin);

        var response = await RemoveAsync(client, admin, otherStore, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Store.MemberNotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal((RoleNames.StoreUser, 1L), await StateOfAsync(email));
    }

    [Fact]
    public async Task ACustomerWhoRunsNoStore_AndAnUnknownUser_Are404()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var customerEmail = UniqueEmail();
        await RegisterAsync(client, customerEmail);
        var customerId = await PostgresServer.ScalarAsync<Guid>(
            await PostgresServer.ApplicationConnectionStringAsync(), $"select id from users where email = '{customerEmail}'");

        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(client, admin, storeId, customerId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(client, admin, storeId, Guid.CreateVersion7())).StatusCode);
        Assert.Equal((RoleNames.Customer, 0L), await StateOfAsync(customerEmail));
    }

    [Fact]
    public async Task ManyAdminsRemovingTheSameMemberAtTheSameTime_OneGets204_AndTheRestGet404()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (storeId, userId, email) = await AStoreUserAsync(client, admin);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RemoveAsync(client, admin, storeId, userId)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.NotFound));
        Assert.Equal((RoleNames.Customer, 0L), await StateOfAsync(email)); // never a customer with a store, or a store user without one
    }
}
