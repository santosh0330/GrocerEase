using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The orders of every store, read by the admin, against the real API and PostgreSQL. <c>orders.read.all</c> is the admin's alone: a store
/// user, who may read the orders of their own store, and a customer are refused. It is read only. The database is shared with other tests,
/// so a test that cares about an exact page narrows it to its own store with <c>?storeId=</c>.
/// </summary>
public class AdminOrdersEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<OrderResponse> PlaceOneAsync(HttpClient client, string customer, Shop shop) =>
        await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 1))));

    private static async Task<PagedResponse<OrderResponse>> ListAsync(HttpClient client, string token, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/admin/orders{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>())!;
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/admin/orders", null)).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("storeUser")]
    public async Task OnlyTheAdminReadsEveryStoresOrders_ACustomerOrAStoreUserIs403(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        await PlaceOneAsync(client, customer, shop);
        var token = string.Equals(who, "storeUser", StringComparison.Ordinal) ? shop.StoreUser : customer;

        var response = await SendAsync(client, HttpMethod.Get, "/admin/orders", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheAdmin_SeesTheOrdersOfEveryStore_NewestFirst_WithTheirLines()
    {
        using var client = factory.CreateClient();
        var (a, b) = (await AShopAsync(client), await AShopAsync(client));
        var first = await PlaceOneAsync(client, (await RegisterAsync(client)).Token, a);
        var second = await PlaceOneAsync(client, (await RegisterAsync(client)).Token, b);

        var page = await ListAsync(client, a.Admin, "?pageSize=100");

        var ids = page.Items.Select(order => order.Id).ToList();
        Assert.Contains(first.Id, ids);
        Assert.Contains(second.Id, ids);
        Assert.True(ids.IndexOf(second.Id) < ids.IndexOf(first.Id), "the newest comes first");
        Assert.Equal(("Pending", b.StoreId, 1), (page.Items[ids.IndexOf(second.Id)].Status, page.Items[ids.IndexOf(second.Id)].StoreId, page.Items[ids.IndexOf(second.Id)].Lines.Single().Quantity));
    }

    [Fact]
    public async Task TheStoreFilter_ShowsOnlyThatStore_AndAnUnknownStoreIsAnEmptyPage_NotA404()
    {
        using var client = factory.CreateClient();
        var (a, b) = (await AShopAsync(client), await AShopAsync(client));
        var inA = await PlaceOneAsync(client, (await RegisterAsync(client)).Token, a);
        await PlaceOneAsync(client, (await RegisterAsync(client)).Token, b);

        var onlyA = await ListAsync(client, a.Admin, $"?storeId={a.StoreId}");
        var unknown = await ListAsync(client, a.Admin, $"?storeId={Guid.CreateVersion7()}");

        Assert.Equal([inA.Id], onlyA.Items.Select(order => order.Id));
        Assert.Equal(1, onlyA.TotalCount);
        Assert.Empty(unknown.Items);
        Assert.Equal(0, unknown.TotalCount);
    }

    [Fact]
    public async Task TheStatusFilter_ShowsOnlyThatStatus_InAnyCase_AndCombinesWithTheStore()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var pending = await PlaceOneAsync(client, customer, shop);
        var verified = await PlaceOneAsync(client, customer, shop);
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = 'Verified' where id = '{verified.Id}'");

        var onlyPending = await ListAsync(client, shop.Admin, $"?storeId={shop.StoreId}&status=Pending");
        var onlyVerified = await ListAsync(client, shop.Admin, $"?storeId={shop.StoreId}&status=verified");
        var noneShipped = await ListAsync(client, shop.Admin, $"?storeId={shop.StoreId}&status=SHIPPED");

        Assert.Equal([pending.Id], onlyPending.Items.Select(order => order.Id));
        Assert.Equal(1, onlyPending.TotalCount);
        Assert.Equal([verified.Id], onlyVerified.Items.Select(order => order.Id));
        Assert.Equal(0, noneShipped.TotalCount);
    }

    [Fact]
    public async Task TheList_IsPaged()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await PlaceOneAsync(client, customer, shop)).Id);
        }

        var second = await ListAsync(client, shop.Admin, $"?storeId={shop.StoreId}&page=2&pageSize=2");

        Assert.Equal([ids[0]], second.Items.Select(order => order.Id)); // newest first, so the oldest is on the last page
        Assert.Equal((2, 2, 3), (second.Page, second.PageSize, second.TotalCount));
    }

    [Theory]
    [InlineData("status=Lost", "status")]
    [InlineData("status=1", "status")]
    [InlineData("status=Pending,Verified", "status")]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("storeId=not-a-guid", "storeId")]
    [InlineData("storeId=00000000-0000-0000-0000-000000000000", "storeId")]
    public async Task ABadFilterOrPage_Is400_NamingTheField(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);

        var response = await SendAsync(client, HttpMethod.Get, $"/admin/orders?{query}", admin);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }

    [Theory]
    [InlineData("POST", "/admin/orders")]
    [InlineData("POST", "/admin/orders/{id}/verify")]
    [InlineData("POST", "/admin/orders/{id}/ship")]
    [InlineData("POST", "/admin/orders/{id}/cancel")]
    [InlineData("PUT", "/admin/orders/{id}")]
    [InlineData("DELETE", "/admin/orders/{id}")]
    public async Task TheAdminCannotMoveOrChangeAnOrder_ThereIsNoSuchRoute(string method, string path)
    {
        using var client = factory.CreateClient();
        var admin = await AdminTokenAsync(client);

        var response = await SendAsync(client, new HttpMethod(method), path.Replace("{id}", Guid.CreateVersion7().ToString(), StringComparison.Ordinal), admin);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{method} {path} gave {(int)response.StatusCode}");
    }

    [Fact]
    public async Task TheAdmin_StillCannotUseTheStoreRoute_ReadingEveryOrderIsNotManagingAStore()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/orders", shop.Admin)).StatusCode);
    }
}
