using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Products;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Adding and reading one product, against the real API and PostgreSQL. The point is who may do what: <c>products.manage</c> says
/// what kind of user may add a product, and only being one of the people who run <i>that</i> store says it may add one <i>there</i>.
/// A store the caller does not run is a 404, never a 403.
/// </summary>
public class ProductsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] ManageOnly = ["products.manage"];

    private static CreateProductRequest Milk(string? name = null) => new(name ?? $"Milk {Guid.NewGuid():N}", "Fresh milk", 2.50m);

    private static async Task<long> ProductsNamedAsync(string name) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from products where name = '{name}'");

    [Fact]
    public async Task Adding_WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{Guid.CreateVersion7()}/products", token: null, Milk());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task Adding_AsACustomerOrAnAdmin_Is403_AndAddsNothing(string who)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? admin : (await RegisterAsync(client)).Token;
        var request = Milk();

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(0L, await ProductsNamedAsync(request.Name));
    }

    [Fact]
    public async Task AStoreUser_AddsAProductToTheirOwnStore_AndGets201WithItsLocation()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var name = $"Milk {Guid.NewGuid():N}";

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, new CreateProductRequest($"  {name} ", " Fresh milk ", 2.50m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal((storeId, name, "Fresh milk", 2.50m), (product.StoreId, product.Name, product.Description, product.Price)); // stored trimmed
        Assert.EndsWith($"/stores/{storeId}/products/{product.Id}", response.Headers.Location?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDescription_IsOptional()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, new { name = $"Milk {Guid.NewGuid():N}", price = 1.2m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(string.Empty, (await response.Content.ReadFromJsonAsync<ProductResponse>())!.Description);
    }

    [Fact]
    public async Task AStoreUserOfAnotherStore_Is404_AndAddsNothing_BecauseThePermissionIsNotEnough()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var theirs = await CreateStoreAsync(client, admin);
        var other = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, theirs);
        var request = Milk();

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{other}/products", token, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // not 403: that would say the store exists
        Assert.Equal("Store.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(0L, await ProductsNamedAsync(request.Name));
    }

    [Fact]
    public async Task AnUnknownStore_GivesTheSameAnswerAsAStoreThatIsNotTheirs()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (token, _, _) = await StoreUserOfAsync(client, admin, await CreateStoreAsync(client, admin));

        var unknown = await SendAsync(client, HttpMethod.Post, $"/stores/{Guid.CreateVersion7()}/products", token, Milk());
        var notTheirs = await SendAsync(client, HttpMethod.Post, $"/stores/{await CreateStoreAsync(client, admin)}/products", token, Milk());

        var (unknownBody, notTheirsBody) = (await ProblemAsync(unknown), await ProblemAsync(notTheirs));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
        // Nothing tells the two apart: the same status, code and text, so a store user cannot probe which stores exist.
        Assert.Equal(
            (unknownBody.GetProperty("code").GetString(), unknownBody.GetProperty("detail").GetString()),
            (notTheirsBody.GetProperty("code").GetString(), notTheirsBody.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task AStoreUserTakenOutOfTheStore_IsRefusedAtOnce_EvenWithATokenThatStillSaysStoreUser()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, userId, _) = await StoreUserOfAsync(client, admin, storeId);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk())).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/members/{userId}", admin)).StatusCode);

        // The token is a copy and is still valid for minutes, with products.manage in it. Only the database knows they are out.
        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ASignedTokenWithThePermission_ForSomeoneWhoRunsNoStore_Is404()
    {
        using var client = factory.CreateClient();
        var storeId = await CreateStoreAsync(client, await AdminTokenAsync(client));
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { ["permission"] = ManageOnly });

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ASignedTokenWithThePermission_ThatDoesNotNameAUser_Is401()
    {
        using var client = factory.CreateClient();
        var storeId = await CreateStoreAsync(client, await AdminTokenAsync(client));
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = "not-an-id", ["permission"] = ManageOnly });

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Authentication.InvalidToken", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameNameTwiceInOneStore_Is409_ButAnotherStoreMayUseIt()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var otherStore = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var (otherToken, _, _) = await StoreUserOfAsync(client, admin, otherStore);
        var request = Milk();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, request)).StatusCode);

        var duplicate = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, request with { Price = 9m });
        var elsewhere = await SendAsync(client, HttpMethod.Post, $"/stores/{otherStore}/products", otherToken, request);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Product.DuplicateName", (await ProblemAsync(duplicate)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task EightStoreUsersAddingTheSameNameAtTheSameTime_OneGets201_AndTheRestGet409()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var request = Milk();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, request)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1L, await ProductsNamedAsync(request.Name));
    }

    [Theory]
    [InlineData("", 1.0, "name")]
    [InlineData("   ", 1.0, "name")]
    [InlineData("Milk", 0.0, "price")]
    [InlineData("Milk", -1.0, "price")]
    [InlineData("Milk", 1.005, "price")]
    [InlineData("Milk", 10000000000.00, "price")]
    public async Task ABadNameOrPrice_Is400_WithTheFieldNamed(string name, double price, string field)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, new { name, description = "", price = (decimal)price });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task ATooLongNameOrDescription_Is400()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);

        var response = await SendAsync(
            client, HttpMethod.Post, $"/stores/{storeId}/products", token, new CreateProductRequest(new string('a', 101), new string('b', 1001), 1m));

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(errors.TryGetProperty("name", out _) && errors.TryGetProperty("description", out _));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("customer")]
    [InlineData("storeuser-of-another-store")]
    public async Task EveryRole_MayReadAProduct_OfAnyStore(string who)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (owner, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var created = (await (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", owner, Milk())).Content.ReadFromJsonAsync<ProductResponse>())!;
        var token = who switch
        {
            "admin" => admin,
            "customer" => (await RegisterAsync(client)).Token,
            _ => (await StoreUserOfAsync(client, admin, await CreateStoreAsync(client, admin))).Token,
        };

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products/{created.Id}", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created, await response.Content.ReadFromJsonAsync<ProductResponse>());
    }

    [Fact]
    public async Task Reading_WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/products/{Guid.CreateVersion7()}", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownProduct_AndAProductThroughTheWrongStore_AreBoth404_WithTheSameCode()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (owner, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var created = (await (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", owner, Milk())).Content.ReadFromJsonAsync<ProductResponse>())!;
        var customer = (await RegisterAsync(client)).Token;

        var unknown = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products/{Guid.CreateVersion7()}", customer);
        var wrongStore = await SendAsync(client, HttpMethod.Get, $"/stores/{await CreateStoreAsync(client, admin)}/products/{created.Id}", customer);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongStore.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(unknown)).GetProperty("code").GetString());
        Assert.Equal("Product.NotFound", (await ProblemAsync(wrongStore)).GetProperty("code").GetString());
    }

    private static async Task<string> StoreWithProductsAsync(HttpClient client, string admin, Guid storeId, params string[] names)
    {
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        foreach (var name in names)
        {
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk(name))).StatusCode);
        }

        return token;
    }

    private static async Task<PagedResponse<ProductResponse>> PageAsync(HttpClient client, string token, Guid storeId, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ProductResponse>>())!;
    }

    [Fact]
    public async Task ACustomer_BrowsesTheProductsOfAStore_APageAtATime_InOrderOfName()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        await StoreWithProductsAsync(client, admin, storeId, "Eggs", "Bread", "Milk", "Apples", "Cheese");
        var customer = (await RegisterAsync(client)).Token;

        var first = await PageAsync(client, customer, storeId, "?page=1&pageSize=2");
        var second = await PageAsync(client, customer, storeId, "?page=2&pageSize=2");
        var last = await PageAsync(client, customer, storeId, "?page=3&pageSize=2");

        Assert.Equal(["Apples", "Bread"], first.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal(["Cheese", "Eggs"], second.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal(["Milk"], last.Items.Select(product => product.Name), StringComparer.Ordinal);
        Assert.Equal((5, 5, 5), (first.TotalCount, second.TotalCount, last.TotalCount));
        Assert.Equal((3, 2), (last.Page, last.PageSize));
    }

    [Fact]
    public async Task WithNoPagingGiven_ItIsThePageOneOfTwentyItems_AndAPagePastTheEndIsEmpty()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        await StoreWithProductsAsync(client, admin, storeId, "Milk");
        var customer = (await RegisterAsync(client)).Token;

        var defaults = await PageAsync(client, customer, storeId);
        var past = await PageAsync(client, customer, storeId, "?page=99");

        Assert.Equal((1, 20, 1, 1), (defaults.Page, defaults.PageSize, defaults.TotalCount, defaults.Items.Count));
        Assert.Empty(past.Items);
        Assert.Equal(1, past.TotalCount);
    }

    [Fact]
    public async Task TheList_ShowsOnlyThatStoresProducts()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var other = await CreateStoreAsync(client, admin);
        await StoreWithProductsAsync(client, admin, storeId, "Milk");
        await StoreWithProductsAsync(client, admin, other, "Elsewhere", "Also elsewhere");

        var page = await PageAsync(client, (await RegisterAsync(client)).Token, storeId);

        Assert.Equal(["Milk"], page.Items.Select(product => product.Name), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?page=-1", "page")]
    [InlineData("?page=100001", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task PagingOutsideTheLimits_Is400_NamingTheParameter(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products{query}", (await RegisterAsync(client)).Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }

    [Fact]
    public async Task PagingThatIsNotANumber_Is400()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products?page=abc", (await RegisterAsync(client)).Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheLargestPageSize_IsAccepted()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);

        var page = await PageAsync(client, (await RegisterAsync(client)).Token, storeId, "?pageSize=100");

        Assert.Equal(100, page.PageSize);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("storeuser-of-another-store")]
    public async Task EveryRole_MayBrowse_AndAnUnknownStoreIs404(string who)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var token = string.Equals(who, "admin", StringComparison.Ordinal)
            ? admin
            : (await StoreUserOfAsync(client, admin, await CreateStoreAsync(client, admin))).Token;

        var known = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products", token);
        var unknown = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/products", token);

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(unknown)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Browsing_WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/products", token: null)).StatusCode);
    }

    private static async Task<ProductResponse> AddProductAsync(HttpClient client, string token, Guid storeId, string? name = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string? token, Guid storeId, Guid productId, object body) =>
        SendAsync(client, HttpMethod.Put, $"/stores/{storeId}/products/{productId}", token, body);

    private static async Task<ProductResponse?> ReadAsync(HttpClient client, string token, Guid storeId, Guid productId)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products/{productId}", token);
        return response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<ProductResponse>() : null;
    }

    [Fact]
    public async Task AStoreUser_ArchivesTheirProduct_Which204s_LeavesTheCatalogue_AndFreesItsName()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        var customer = (await RegisterAsync(client)).Token;

        var response = await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{milk.Id}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await ReadAsync(client, customer, storeId, milk.Id)); // not found any more
        Assert.Empty((await PageAsync(client, customer, storeId)).Items);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, Milk(milk.Name))).StatusCode); // the name is free
    }

    [Fact]
    public async Task Archiving_WithNoToken_Is401_AndACustomerOrAnAdminIs403_AndTheProductStays()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        var path = $"/stores/{storeId}/products/{milk.Id}";

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Delete, path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, path, (await RegisterAsync(client)).Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, path, admin)).StatusCode);
        Assert.Equal(milk, await ReadAsync(client, token, storeId, milk.Id));
    }

    [Fact]
    public async Task AStoreUserOfAnotherStore_CannotArchiveTheProduct_Is404_AndItStaysOnSale()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var theirs = await CreateStoreAsync(client, admin);
        var other = await CreateStoreAsync(client, admin);
        var (ownerToken, _, _) = await StoreUserOfAsync(client, admin, other);
        var (intruder, _, _) = await StoreUserOfAsync(client, admin, theirs);
        var milk = await AddProductAsync(client, ownerToken, other);

        var viaTheirStore = await SendAsync(client, HttpMethod.Delete, $"/stores/{theirs}/products/{milk.Id}", intruder);
        var viaTheStore = await SendAsync(client, HttpMethod.Delete, $"/stores/{other}/products/{milk.Id}", intruder);

        Assert.Equal(HttpStatusCode.NotFound, viaTheirStore.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaTheStore.StatusCode);
        Assert.Equal(milk, await ReadAsync(client, ownerToken, other, milk.Id));
    }

    [Fact]
    public async Task ArchivingTwice_IsA404_NotASecondSuccess_AndEightAtOnce_OneGets204()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var first = await AddProductAsync(client, token, storeId);
        var second = await AddProductAsync(client, token, storeId);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{first.Id}", token)).StatusCode);
        var again = await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{first.Id}", token);
        var many = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{second.Id}", token)));

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(again)).GetProperty("code").GetString());
        Assert.Equal(1, many.Count(response => response.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(7, many.Count(response => response.StatusCode == HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task AnArchivedProduct_CannotBeEdited_OrBroughtBack()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{milk.Id}", token);

        var response = await PutAsync(client, token, storeId, milk.Id, new UpdateProductRequest("Back", "", 1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await ReadAsync(client, token, storeId, milk.Id));
    }

    [Fact]
    public async Task AStoreUser_EditsTheirProduct_AndTheChangeIsWhatIsReadBack()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        var name = $"Oat {Guid.NewGuid():N}";

        var response = await PutAsync(client, token, storeId, milk.Id, new UpdateProductRequest($" {name} ", "Vegan", 3.20m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
        Assert.Equal((milk.Id, storeId, name, "Vegan", 3.20m), (edited.Id, edited.StoreId, edited.Name, edited.Description, edited.Price));
        Assert.Equal(edited, await ReadAsync(client, token, storeId, milk.Id));
    }

    [Fact]
    public async Task Editing_WithNoToken_Is401_AndACustomerOrAnAdminIs403_AndChangesNothing()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        var body = new UpdateProductRequest("Hacked", "", 1m);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(client, null, storeId, milk.Id, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, (await RegisterAsync(client)).Token, storeId, milk.Id, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, admin, storeId, milk.Id, body)).StatusCode);
        Assert.Equal(milk, await ReadAsync(client, token, storeId, milk.Id));
    }

    [Fact]
    public async Task AStoreUserOfAnotherStore_CannotEditTheProduct_Is404_AndItIsUnchanged()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var theirs = await CreateStoreAsync(client, admin);
        var other = await CreateStoreAsync(client, admin);
        var (ownerToken, _, _) = await StoreUserOfAsync(client, admin, other);
        var (intruder, _, _) = await StoreUserOfAsync(client, admin, theirs);
        var milk = await AddProductAsync(client, ownerToken, other);

        var viaTheirStore = await PutAsync(client, intruder, theirs, milk.Id, new UpdateProductRequest("Hacked", "", 1m)); // a store they run, a product they do not own
        var viaTheStore = await PutAsync(client, intruder, other, milk.Id, new UpdateProductRequest("Hacked", "", 1m)); // the right store, which they do not run

        Assert.Equal(HttpStatusCode.NotFound, viaTheirStore.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(viaTheirStore)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, viaTheStore.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(viaTheStore)).GetProperty("code").GetString());
        Assert.Equal(milk, await ReadAsync(client, ownerToken, other, milk.Id));
    }

    [Fact]
    public async Task AnUnknownProduct_Is404()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);

        var response = await PutAsync(client, token, storeId, Guid.CreateVersion7(), new UpdateProductRequest("x", "", 1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ARenameToAnotherProductsName_Is409_ButToItsOwnNameIsFine()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        var bread = await AddProductAsync(client, token, storeId);

        var clash = await PutAsync(client, token, storeId, milk.Id, new UpdateProductRequest(bread.Name, "", 1m));
        var same = await PutAsync(client, token, storeId, milk.Id, new UpdateProductRequest(milk.Name, "New text", 9m));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("Product.DuplicateName", (await ProblemAsync(clash)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task AStoreUserTakenOut_CannotEditAtOnce_EvenWithATokenThatStillSaysStoreUser()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, userId, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/members/{userId}", admin);

        var response = await PutAsync(client, token, storeId, milk.Id, new UpdateProductRequest("Hacked", "", 1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(milk.Name, (await ReadAsync(client, admin, storeId, milk.Id))?.Name);
    }

    [Theory]
    [InlineData("", 1.0, "name")]
    [InlineData("Milk", 0.0, "price")]
    [InlineData("Milk", 1.005, "price")]
    public async Task EditingWithABadNameOrPrice_Is400_AndChangesNothing(string name, double price, string field)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await AddProductAsync(client, token, storeId);

        var response = await PutAsync(client, token, storeId, milk.Id, new { name, description = "", price = (decimal)price });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(milk, await ReadAsync(client, token, storeId, milk.Id));
    }
}
