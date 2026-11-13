using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Changing the stock of a product, against the real API and PostgreSQL. <c>inventory.manage</c> says what kind of user may; only
/// running <i>that</i> store says it may there, and a store the caller does not run is a 404, never a 403. A change is an amount, never a
/// new total, and the quantity is never driven below zero, however many requests arrive at once.
/// </summary>
public class StockAdjustmentEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] InventoryOnly = ["inventory.manage"];

    private static Task<HttpResponseMessage> AdjustAsync(HttpClient client, string? token, Guid storeId, Guid productId, object body) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products/{productId}/stock-adjustments", token, body);

    private static async Task<StockResponse> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StockResponse>())!;
    }

    private static async Task<int?> QuantityAsync(Guid productId) =>
        await PostgresServer.ScalarAsync<int?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select quantity_on_hand from inventory_items where product_id = '{productId}'");

    private static async Task<(string Admin, Guid StoreId, string Token, Guid ProductId)> AStoreWithAProductAsync(HttpClient client)
    {
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        return (admin, storeId, token, (await CreateProductAsync(client, token, storeId)).Id);
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        var response = await AdjustAsync(client, token: null, Guid.CreateVersion7(), Guid.CreateVersion7(), new AdjustStockRequest(1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task ACustomerOrAnAdmin_Is403_AndChangesNothing(string who)
    {
        using var client = factory.CreateClient();
        var (admin, storeId, _, productId) = await AStoreWithAProductAsync(client);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? admin : (await RegisterAsync(client)).Token;

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(5));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task AStoreUser_AddsAndRemovesStock_InTheirOwnStore()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);

        var added = await OkAsync(await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(12)));
        var removed = await OkAsync(await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(-5)));

        Assert.Equal(new StockResponse(productId, 12), added);
        Assert.Equal(new StockResponse(productId, 7), removed);
        Assert.Equal(7, await QuantityAsync(productId));
    }

    [Fact]
    public async Task RemovingMoreThanThereIs_Is409_NotEnoughStock_AndChangesNothing()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);
        await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(5));

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(-6));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Inventory.NotEnoughStock", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(5, await QuantityAsync(productId));
    }

    [Fact]
    public async Task RemovingFromAProductThatHasNeverHadStock_Is409_AndMakesNoRow()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(-1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task AddingPastTheMaximum_Is409_TooMuchStock()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);
        await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(1_000_000_000));

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Inventory.TooMuchStock", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000_001)]
    [InlineData(-1_000_000_001)]
    public async Task AZeroOrAnEnormousChange_Is400_NamingTheField(int delta)
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(delta));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("delta", out _));
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task ANewTotalIsNotAnAmount_ForExample_ASetToFieldDoesNothing()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);

        // The API has no "set the quantity to": a body without a delta is a change of zero, which is refused.
        var response = await AdjustAsync(client, token, storeId, productId, new { quantity = 40 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task AStoreUserOfAnotherStore_Is404_AndChangesNothing_BecauseThePermissionIsNotEnough()
    {
        using var client = factory.CreateClient();
        var (admin, storeId, ownerToken, productId) = await AStoreWithAProductAsync(client);
        await AdjustAsync(client, ownerToken, storeId, productId, new AdjustStockRequest(5));
        var (intruder, _, _) = await StoreUserOfAsync(client, admin, await CreateStoreAsync(client, admin));

        var viaTheStore = await AdjustAsync(client, intruder, storeId, productId, new AdjustStockRequest(-5)); // the right store, which they do not run

        Assert.Equal(HttpStatusCode.NotFound, viaTheStore.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(viaTheStore)).GetProperty("code").GetString());
        Assert.Equal(5, await QuantityAsync(productId));
    }

    [Fact]
    public async Task AProductOfAnotherStore_ThroughTheCallersOwnStore_Is404_AndIsUntouched()
    {
        using var client = factory.CreateClient();
        var (admin, storeId, ownerToken, productId) = await AStoreWithAProductAsync(client);
        await AdjustAsync(client, ownerToken, storeId, productId, new AdjustStockRequest(5));
        var theirs = await CreateStoreAsync(client, admin);
        var (intruder, _, _) = await StoreUserOfAsync(client, admin, theirs);

        var response = await AdjustAsync(client, intruder, theirs, productId, new AdjustStockRequest(-5)); // their store, someone else's product

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(5, await QuantityAsync(productId));
    }

    [Fact]
    public async Task AnUnknownProduct_AndAnArchivedOne_AreNotFound()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{productId}", token);

        var unknown = await AdjustAsync(client, token, storeId, Guid.CreateVersion7(), new AdjustStockRequest(1));
        var archived = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(1));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, archived.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(archived)).GetProperty("code").GetString());
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task AStoreUserTakenOut_CannotChangeStockAtOnce_EvenWithATokenThatStillSaysStoreUser()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, userId, _) = await StoreUserOfAsync(client, admin, storeId);
        var productId = (await CreateProductAsync(client, token, storeId)).Id;
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/members/{userId}", admin);

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task ASignedTokenWithThePermission_ForSomeoneWhoRunsNoStore_Is404()
    {
        using var client = factory.CreateClient();
        var (_, storeId, _, productId) = await AStoreWithAProductAsync(client);
        var token = TestTokens.Create(extraClaims: new Dictionary<string, object>(StringComparer.Ordinal) { ["permission"] = InventoryOnly });

        var response = await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await QuantityAsync(productId));
    }

    [Fact]
    public async Task TwentyRemovalsAtTheSameTime_FromStockOfFive_FiveGet200_FifteenGet409_AndTheQuantityIsZero()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);
        await AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(5));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(-1))));

        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(15, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await QuantityAsync(productId)); // never below zero, never a lost change
    }

    [Fact]
    public async Task EightAddsAtTheSameTime_AllCount()
    {
        using var client = factory.CreateClient();
        var (_, storeId, token, productId) = await AStoreWithAProductAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AdjustAsync(client, token, storeId, productId, new AdjustStockRequest(3))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(24, await QuantityAsync(productId));
    }

    private static async Task<PagedResponse<InventoryEntryResponse>> InventoryAsync(HttpClient client, string token, Guid storeId, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/inventory{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<InventoryEntryResponse>>())!;
    }

    [Fact]
    public async Task TheInventory_ListsEveryProductOnSale_WithItsQuantity_ZeroWhenItHasNone_InOrderOfName()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await CreateProductAsync(client, token, storeId, "Milk");
        var bread = await CreateProductAsync(client, token, storeId, "Bread");
        var gone = await CreateProductAsync(client, token, storeId, "Gone");
        await AdjustAsync(client, token, storeId, milk.Id, new AdjustStockRequest(7));
        await AdjustAsync(client, token, storeId, gone.Id, new AdjustStockRequest(3));
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/products/{gone.Id}", token); // archived: leaves the list, with its stock

        var page = await InventoryAsync(client, token, storeId);

        Assert.Equal([new InventoryEntryResponse(bread.Id, "Bread", 0, null, false), new InventoryEntryResponse(milk.Id, "Milk", 7, null, false)], page.Items);
        Assert.Equal((1, 20, 2), (page.Page, page.PageSize, page.TotalCount));
    }

    [Fact]
    public async Task TheInventory_IsPaged()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        foreach (var name in new[] { "E", "B", "D", "A", "C" })
        {
            var product = await CreateProductAsync(client, token, storeId, name);
            await AdjustAsync(client, token, storeId, product.Id, new AdjustStockRequest(name[0] - 'A' + 1)); // A=1 ... E=5
        }

        var second = await InventoryAsync(client, token, storeId, "?page=2&pageSize=2");
        var past = await InventoryAsync(client, token, storeId, "?page=9");

        Assert.Equal([("C", 3), ("D", 4)], second.Items.Select(entry => (entry.ProductName, entry.QuantityOnHand)));
        Assert.Equal(5, second.TotalCount);
        Assert.Empty(past.Items);
    }

    [Fact]
    public async Task TheInventory_ShowsOnlyThisStoresStock()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var (storeA, storeB) = (await CreateStoreAsync(client, admin), await CreateStoreAsync(client, admin));
        var (tokenA, _, _) = await StoreUserOfAsync(client, admin, storeA);
        var (tokenB, _, _) = await StoreUserOfAsync(client, admin, storeB);
        var milkA = await CreateProductAsync(client, tokenA, storeA, "Milk");
        var milkB = await CreateProductAsync(client, tokenB, storeB, "Milk");
        await AdjustAsync(client, tokenA, storeA, milkA.Id, new AdjustStockRequest(7));
        await AdjustAsync(client, tokenB, storeB, milkB.Id, new AdjustStockRequest(99));

        var a = await InventoryAsync(client, tokenA, storeA);
        var b = await InventoryAsync(client, tokenB, storeB);

        Assert.Equal([new InventoryEntryResponse(milkA.Id, "Milk", 7, null, false)], a.Items);
        Assert.Equal([new InventoryEntryResponse(milkB.Id, "Milk", 99, null, false)], b.Items);
    }

    [Fact]
    public async Task TheInventory_WithNoToken_Is401_AndACustomerOrAnAdminIs403()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var path = $"/stores/{storeId}/inventory";

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, path, (await RegisterAsync(client)).Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, path, admin)).StatusCode);
    }

    [Fact]
    public async Task TheInventory_OfAStoreThatIsNotTheirs_Is404_TheSameAsAStoreThatDoesNotExist_AndATakenOutStoreUserIsRefusedAtOnce()
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, await CreateStoreAsync(client, admin)); // runs another store
        var (own, ownUserId, _) = await StoreUserOfAsync(client, admin, storeId);

        var notTheirs = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/inventory", token);
        var unknown = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/inventory", token);
        await SendAsync(client, HttpMethod.Delete, $"/stores/{storeId}/members/{ownUserId}", admin);
        var takenOut = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/inventory", own); // a token that still says StoreUser

        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, takenOut.StatusCode);
        var (a, b) = (await ProblemAsync(notTheirs), await ProblemAsync(unknown));
        Assert.Equal(
            (a.GetProperty("code").GetString(), a.GetProperty("detail").GetString()),
            (b.GetProperty("code").GetString(), b.GetProperty("detail").GetString()));
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task TheInventory_PagingOutsideTheLimits_Is400_NamingTheParameter(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/inventory{query}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }
}
