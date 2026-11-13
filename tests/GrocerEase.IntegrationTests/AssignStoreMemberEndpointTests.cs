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
/// POST /stores/{storeId}/members against the real API and PostgreSQL: an admin assigns a customer to a store, and that makes the
/// customer a store user. What is checked afterwards is read from the database and from the tokens, not from the response alone.
/// </summary>
public class AssignStoreMemberEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    private static async Task<HttpResponseMessage> AssignAsync(HttpClient client, string? token, Guid storeId, string email)
    {
        using var request = Send(HttpMethod.Post, $"/stores/{storeId}/members", token, new AssignStoreMemberRequest(email));
        return await client.SendAsync(request);
    }

    private static async Task<string> RoleInDatabaseAsync(string email) =>
        (await PostgresServer.ScalarAsync<string>(
            await PostgresServer.ApplicationConnectionStringAsync(),
            $"select r.name from users u join roles r on r.id = u.role_id where u.email = '{email}'"))!;

    private static async Task<long> MembershipsAsync(string email) =>
        await PostgresServer.ScalarAsync<long>(
            await PostgresServer.ApplicationConnectionStringAsync(),
            $"select count(*) from store_members m join users u on u.id = m.user_id where u.email = '{email}'");

    private static string RoleOf(string token) => new JsonWebToken(token).GetClaim(JwtClaims.Role).Value;

    private static string[] PermissionsOf(string token) =>
        [.. new JsonWebToken(token).Claims.Where(claim => string.Equals(claim.Type, JwtClaims.Permission, StringComparison.Ordinal)).Select(claim => claim.Value)];

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await AssignAsync(client, token: null, Guid.CreateVersion7(), UniqueEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("storeuser")]
    public async Task ANonAdmin_Is403_AndChangesNothing(string who)
    {
        using var client = factory.CreateClient();
        var storeId = await CreateStoreAsync(client, await AdminTokenAsync(client));
        var target = UniqueEmail();
        await RegisterAsync(client, target);
        var callerEmail = UniqueEmail();
        var caller = await RegisterAsync(client, callerEmail);
        var token = caller.Token;
        if (string.Equals(who, "storeuser", StringComparison.Ordinal))
        {
            await TestUsers.MakeStoreUserAsync(callerEmail);
            token = (await LoginAsync(client, callerEmail)).Token;
        }

        var response = await AssignAsync(client, token, storeId, target);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal((RoleNames.Customer, 0L), (await RoleInDatabaseAsync(target), await MembershipsAsync(target)));
    }

    [Fact]
    public async Task AnAdmin_AssignsACustomer_WhoBecomesAStoreUserOfThatStore()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var response = await AssignAsync(client, admin, storeId, email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = (await response.Content.ReadFromJsonAsync<StoreMemberResponse>())!;
        Assert.Equal((storeId, "Asha", "Rai", email), (member.StoreId, member.FirstName, member.LastName, member.Email));
        Assert.Equal(RoleNames.StoreUser, await RoleInDatabaseAsync(email));
        Assert.Equal(1L, await MembershipsAsync(email));
        Assert.DoesNotContain("password", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheCustomersOldToken_StillSaysCustomer_UntilANewOneIsMade_AndRefreshingPicksTheRoleUp()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var email = UniqueEmail();
        var before = await RegisterAsync(client, email);

        await AssignAsync(client, admin, storeId, email);

        Assert.Equal(RoleNames.Customer, RoleOf(before.Token)); // a token is a copy: it was made while they were a customer
        var refreshed = await (await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(before.RefreshToken)))
            .Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(RoleNames.StoreUser, RoleOf(refreshed!.Token));
        Assert.Equal(RoleNames.StoreUser, RoleOf((await LoginAsync(client, email)).Token));
        Assert.Equal(["products.manage", "products.read", "profile.read", "stores.read"], PermissionsOf(refreshed.Token), StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheEmail_IsFoundWhateverItsCase()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var response = await AssignAsync(client, admin, storeId, $"  {email.ToUpperInvariant()} ");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownStore_Is404_AndTheCustomerIsUntouched()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var response = await AssignAsync(client, admin, Guid.CreateVersion7(), email);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal((RoleNames.Customer, 0L), (await RoleInDatabaseAsync(email), await MembershipsAsync(email)));
    }

    [Fact]
    public async Task AnUnknownEmail_Is404()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);

        var response = await AssignAsync(client, admin, await CreateStoreAsync(client, admin), UniqueEmail());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("User.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnAdmin_CannotBeAssigned_AndStaysAnAdmin()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var otherAdmin = UniqueEmail();
        await RegisterAsync(client, otherAdmin);
        await TestUsers.MakeAdminAsync(otherAdmin);

        var response = await AssignAsync(client, admin, await CreateStoreAsync(client, admin), otherAdmin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("User.NotACustomer", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal((RoleNames.Admin, 0L), (await RoleInDatabaseAsync(otherAdmin), await MembershipsAsync(otherAdmin)));
    }

    [Fact]
    public async Task AStoreUser_CannotBeAssignedAgain_NotToTheSameStoreNorToAnother()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var first = await CreateStoreAsync(client, admin);
        var second = await CreateStoreAsync(client, admin);
        var email = UniqueEmail();
        await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(client, admin, first, email)).StatusCode);

        var again = await AssignAsync(client, admin, first, email);
        var elsewhere = await AssignAsync(client, admin, second, email);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, elsewhere.StatusCode);
        Assert.Equal((RoleNames.StoreUser, 1L), (await RoleInDatabaseAsync(email), await MembershipsAsync(email)));
    }

    [Fact]
    public async Task ABadEmail_Is400_WithTheFieldNamed()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);

        var response = await AssignAsync(client, admin, await CreateStoreAsync(client, admin), "not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task ManyAdminsAssigningTheSameCustomerToDifferentStoresAtTheSameTime_OneWins()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var stores = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CreateStoreAsync(client, admin)));
        var email = UniqueEmail();
        await RegisterAsync(client, email);

        var responses = await Task.WhenAll(stores.Select(store => AssignAsync(client, admin, store, email)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal((RoleNames.StoreUser, 1L), (await RoleInDatabaseAsync(email), await MembershipsAsync(email))); // never a role without a store, or the reverse
    }
}
