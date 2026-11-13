using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Authentication;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Stores;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The stores routes against the real API and PostgreSQL: who may do what. No token is a 401; a signed-in user without the
/// permission is a 403 (a customer and a store user may read stores, only an admin may create one). Nothing in the application makes
/// an admin yet, so a test promotes a user with SQL.
/// </summary>
public class StoresEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "Sup3r-s3cret!";

    private static readonly string[] ManageOnly = ["stores.manage"];

    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static string UniqueName() => $"Store {Guid.NewGuid():N}";

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<string> TokenAsync(HttpClient client, Func<string, Task>? promote = null)
    {
        var email = UniqueEmail();
        var register = await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative), new RegisterRequest("Ada", "Lovelace", email, Password));
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        if (promote is null)
        {
            return (await register.Content.ReadFromJsonAsync<AuthenticationResponse>())!.Token;
        }

        await promote(email); // a role is read when a token is made, so sign in again
        var login = await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest(email, Password));
        return (await login.Content.ReadFromJsonAsync<AuthenticationResponse>())!.Token;
    }

    private static Task<string> AdminTokenAsync(HttpClient client) => TokenAsync(client, TestUsers.MakeAdminAsync);

    private static Task<string> StoreUserTokenAsync(HttpClient client) => TokenAsync(client, TestUsers.MakeStoreUserAsync);

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

    private static async Task<StoreResponse> CreateAsync(HttpClient client, string adminToken, string? name = null)
    {
        using var request = Send(HttpMethod.Post, "/stores", adminToken, new CreateStoreRequest(name ?? UniqueName(), "1 High Street"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StoreResponse>())!;
    }

    [Fact]
    public async Task Creating_WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        using var request = Send(HttpMethod.Post, "/stores", token: null, new CreateStoreRequest(UniqueName(), "x"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.Required", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("storeuser")]
    public async Task Creating_AsSomeoneWhoIsNotAnAdmin_Is403_AndCreatesNothing(string who)
    {
        using var client = factory.CreateClient();
        var token = string.Equals(who, "customer", StringComparison.Ordinal) ? await TokenAsync(client) : await StoreUserTokenAsync(client);
        var name = UniqueName();

        using var request = Send(HttpMethod.Post, "/stores", token, new CreateStoreRequest(name, "x"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from stores where name = '{name}'"));
    }

    [Fact]
    public async Task AnAdmin_CreatesAStore_AndGets201_WithItsLocation()
    {
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var name = UniqueName();

        using var request = Send(HttpMethod.Post, "/stores", token, new CreateStoreRequest($"  {name}  ", "1 High Street"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var store = (await response.Content.ReadFromJsonAsync<StoreResponse>())!;
        Assert.Equal((name, "1 High Street"), (store.Name, store.Address)); // stored trimmed
        Assert.EndsWith($"/stores/{store.Id}", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondStoreWithTheSameName_Is409_WithTheDuplicateNameCode()
    {
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var first = await CreateAsync(client, token);

        using var request = Send(HttpMethod.Post, "/stores", token, new CreateStoreRequest(first.Name, "elsewhere"));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Store.DuplicateName", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ABlankOrTooLongName_Is400_WithTheFieldsNamed()
    {
        using var client = factory.CreateClient();
        var token = await AdminTokenAsync(client);

        using var request = Send(HttpMethod.Post, "/stores", token, new CreateStoreRequest(new string('a', 101), ""));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _) && errors.TryGetProperty("address", out _));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("customer")]
    [InlineData("storeuser")]
    public async Task EveryRole_MayReadAStore_AndTheList(string who)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var store = await CreateAsync(client, admin);
        var token = who switch
        {
            "admin" => admin,
            "customer" => await TokenAsync(client),
            _ => await StoreUserTokenAsync(client),
        };

        using var get = Send(HttpMethod.Get, $"/stores/{store.Id}", token);
        using var list = Send(HttpMethod.Get, "/stores", token);
        var one = await client.SendAsync(get);
        var all = await client.SendAsync(list);

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(store, await one.Content.ReadFromJsonAsync<StoreResponse>());
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var first = (await all.Content.ReadFromJsonAsync<PagedResponse<StoreResponse>>())!;
        Assert.Equal((1, 20), (first.Page, first.PageSize));
        Assert.True(first.TotalCount >= 1 && first.Items.Count <= 20);
        Assert.Contains(store, await AllStoresAsync(client, token)); // the database has many stores by now: walk the pages to find it
    }

    private static async Task<List<StoreResponse>> AllStoresAsync(HttpClient client, string token)
    {
        var stores = new List<StoreResponse>();
        for (var page = 1; ; page++)
        {
            using var request = Send(HttpMethod.Get, $"/stores?page={page}&pageSize=100", token);
            var body = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<PagedResponse<StoreResponse>>())!;
            stores.AddRange(body.Items);
            if (body.Items.Count == 0 || stores.Count >= body.TotalCount)
            {
                return stores;
            }
        }
    }

    [Fact]
    public async Task TheList_IsPaged_InOrderOfName_AndAPastTheEndPageIsEmpty()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        await CreateAsync(client, admin);
        await CreateAsync(client, admin);

        using var one = Send(HttpMethod.Get, "/stores?page=1&pageSize=1", admin);
        using var past = Send(HttpMethod.Get, "/stores?page=100000&pageSize=100", admin);
        var first = (await (await client.SendAsync(one)).Content.ReadFromJsonAsync<PagedResponse<StoreResponse>>())!;
        var beyond = (await (await client.SendAsync(past)).Content.ReadFromJsonAsync<PagedResponse<StoreResponse>>())!;

        Assert.Single(first.Items);
        Assert.True(first.TotalCount >= 2);
        Assert.Empty(beyond.Items);
        Assert.True(beyond.TotalCount >= first.TotalCount); // other tests add stores at the same time: the total can only have grown
        // One page is one query, so it is consistent even while other tests add stores; a walk through several pages is not.
        using var wholePage = Send(HttpMethod.Get, "/stores?page=1&pageSize=100", admin);
        var page = (await (await client.SendAsync(wholePage)).Content.ReadFromJsonAsync<PagedResponse<StoreResponse>>())!;
        Assert.Equal(page.Items.OrderBy(store => store.Name, StringComparer.Ordinal).Select(store => store.Id), page.Items.Select(store => store.Id));
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?page=100001", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task PagingOutsideTheLimits_Is400_NamingTheParameter(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var token = await TokenAsync(client);

        using var request = Send(HttpMethod.Get, $"/stores{query}", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }

    [Fact]
    public async Task Reading_WithNoToken_Is401_ForTheListAndForOne()
    {
        using var client = factory.CreateClient();

        using var list = Send(HttpMethod.Get, "/stores", token: null);
        using var one = Send(HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(list)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(one)).StatusCode);
    }

    [Fact]
    public async Task AnUnknownStore_Is404_WithTheNotFoundCode()
    {
        using var client = factory.CreateClient();
        var token = await TokenAsync(client);

        using var request = Send(HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ATokenWithTheManagePermissionAlone_CreatesAStore_BecauseThePermissionDecides_NotTheRoleName()
    {
        using var client = factory.CreateClient();
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["permission"] = ManageOnly,
        });

        using var request = Send(HttpMethod.Post, "/stores", token, new CreateStoreRequest(UniqueName(), "x"));

        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task AnAdminsTokenSignedWithAnotherKey_Is401()
    {
        using var client = factory.CreateClient();
        var forged = TestTokens.Create(
            key: "another-signing-key-0123456789-abcdefghij",
            extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { ["permission"] = ManageOnly });

        using var request = Send(HttpMethod.Post, "/stores", forged, new CreateStoreRequest(UniqueName(), "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }
}
