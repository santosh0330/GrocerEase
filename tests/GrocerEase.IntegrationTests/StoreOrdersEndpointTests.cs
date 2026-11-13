using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The orders of a store, seen by the people who run it, against the real API and PostgreSQL. <c>orders.manage</c> says what kind of user may;
/// only a membership says in which store, and a store that is not yours is a <c>404</c>, identical to one that does not exist.
/// </summary>
public class StoreOrdersEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<OrderResponse> PlaceOneAsync(HttpClient client, string customer, Shop shop, int quantity = 1) =>
        await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, quantity))));

    private static async Task<PagedResponse<OrderResponse>> ListAsync(HttpClient client, string token, Guid storeId, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/orders{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>())!;
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/orders", null)).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserSeesTheOrdersOfAStore_ACustomerOrAnAdminIs403(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        await PlaceOneAsync(client, customer, shop);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : customer;

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/orders", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheStoreUser_SeesTheOrdersOfTheirStore_OldestFirst_FromEveryCustomer_WithTheirLines()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var first = await PlaceOneAsync(client, (await RegisterAsync(client)).Token, shop, 2);
        var second = await PlaceOneAsync(client, (await RegisterAsync(client)).Token, shop, 1);

        var page = await ListAsync(client, shop.StoreUser, shop.StoreId);

        Assert.Equal([first.Id, second.Id], page.Items.Select(order => order.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal((2, 5.00m), (page.Items[0].Lines.Single().Quantity, page.Items[0].Total));
        Assert.All(page.Items, order => Assert.Equal(("Pending", shop.StoreId), (order.Status, order.StoreId)));
    }

    [Fact]
    public async Task TheStoreUser_DoesNotSeeTheOrdersOfAnotherStore_AndAnotherStoreIsTheSame404AsAStoreThatDoesNotExist()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);
        await PlaceOneAsync(client, (await RegisterAsync(client)).Token, theirs);

        Assert.Empty((await ListAsync(client, mine.StoreUser, mine.StoreId)).Items);
        var notMine = await SendAsync(client, HttpMethod.Get, $"/stores/{theirs.StoreId}/orders", mine.StoreUser);
        var unknown = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/orders", mine.StoreUser);

        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (a, b) = (await ProblemAsync(notMine), await ProblemAsync(unknown));
        Assert.Equal((a.GetProperty("code").GetString(), a.GetProperty("detail").GetString()), (b.GetProperty("code").GetString(), b.GetProperty("detail").GetString()));
        Assert.Equal("Store.NotFound", a.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheStatusFilter_ShowsOnlyThatStatus_InAnyCase()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var pending = await PlaceOneAsync(client, customer, shop);
        var verified = await PlaceOneAsync(client, customer, shop);
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = 'Verified' where id = '{verified.Id}'");

        var onlyPending = await ListAsync(client, shop.StoreUser, shop.StoreId, "?status=Pending");
        var onlyVerified = await ListAsync(client, shop.StoreUser, shop.StoreId, "?status=verified");
        var noneShipped = await ListAsync(client, shop.StoreUser, shop.StoreId, "?status=SHIPPED");

        Assert.Equal([pending.Id], onlyPending.Items.Select(order => order.Id));

        Assert.Equal(1, onlyPending.TotalCount);
        Assert.Equal([verified.Id], onlyVerified.Items.Select(order => order.Id));
        Assert.Equal(1, onlyVerified.TotalCount);
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

        var second = await ListAsync(client, shop.StoreUser, shop.StoreId, "?page=2&pageSize=2");

        Assert.Equal([ids[2]], second.Items.Select(order => order.Id));
        Assert.Equal((2, 2, 3), (second.Page, second.PageSize, second.TotalCount));
    }

    [Theory]
    [InlineData("status=Lost", "status")]
    [InlineData("status=1", "status")]
    [InlineData("status=Pending,Verified", "status")]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    public async Task ABadFilterOrPage_Is400_NamingTheField(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/orders?{query}", shop.StoreUser);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }
}
