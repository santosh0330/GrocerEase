using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Orders;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A store user shipping a verified order, against the real API and PostgreSQL. An order must be verified first; shipping is final; and
/// of two store users shipping the same order at the same moment, one wins.
/// </summary>
public class ShipOrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] LosersCodes = ["Order.InvalidTransition", "Order.ChangedMeanwhile"];

    private static Task<HttpResponseMessage> ShipAsync(HttpClient client, string? token, Guid storeId, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/orders/{orderId}/ship", token);

    // The order is placed through the API and put in the status under test directly in the database: this file tests shipping, not verifying.
    private static async Task<OrderResponse> OrderInAsync(HttpClient client, Shop shop, string status, string? customer = null)
    {
        var order = await CreatedAsync(await PlaceAsync(client, customer ?? (await RegisterAsync(client)).Token, shop.StoreId, Order(lines: (shop.Milk.Id, 2))));
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = '{status}' where id = '{order.Id}'");
        return order;
    }

    private static async Task<string?> StatusInDatabaseAsync(Guid orderId) =>
        await PostgresServer.ScalarAsync<string?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select status from orders where id = '{orderId}'");

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await ShipAsync(client, null, Guid.CreateVersion7(), Guid.CreateVersion7())).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserShips_ACustomerOrAnAdminIs403_AndTheOrderStaysVerified(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var order = await OrderInAsync(client, shop, "Verified", customer);

        var response = await ShipAsync(client, string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : customer, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Verified", await StatusInDatabaseAsync(order.Id));
    }

    [Fact]
    public async Task TheStoreUser_ShipsAVerifiedOrder_AndTheStockIsNotTouched()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await OrderInAsync(client, shop, "Verified", customer); // reserved 2: 8 left

        var response = await ShipAsync(client, shop.StoreUser, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var shipped = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((order.Id, "Shipped", order.Total, order.Lines.Count), (shipped.Id, shipped.Status, shipped.Total, shipped.Lines.Count));
        Assert.Equal("Shipped", await StatusInDatabaseAsync(order.Id));
        Assert.Equal(8, await StockAsync(shop.Milk.Id)); // the goods left when the order was placed; shipping moves no stock
        Assert.Equal("Shipped", (await (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", customer)).Content.ReadFromJsonAsync<OrderResponse>())!.Status);
    }

    [Theory]
    [InlineData("Pending")] // it has to be verified first
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task AnOrderThatIsNotVerified_Is409_NamingItsStatus_AndIsNotChanged(string status)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var order = await OrderInAsync(client, shop, status);

        var response = await ShipAsync(client, shop.StoreUser, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("Order.InvalidTransition", problem.GetProperty("code").GetString());
        Assert.Contains(status, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(status, await StatusInDatabaseAsync(order.Id));
    }

    [Fact]
    public async Task AnOrderOfAnotherStore_AndAnUnknownOne_AreTheSame404_AndTheOtherStoreIsNotTouched()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);
        var theirOrder = await OrderInAsync(client, theirs, "Verified");

        var viaMyStore = await ShipAsync(client, mine.StoreUser, mine.StoreId, theirOrder.Id);
        var viaTheirStore = await ShipAsync(client, mine.StoreUser, theirs.StoreId, theirOrder.Id);
        var unknown = await ShipAsync(client, mine.StoreUser, mine.StoreId, Guid.CreateVersion7());

        Assert.All(new[] { viaMyStore, viaTheirStore, unknown }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal("Order.NotFound", (await ProblemAsync(viaMyStore)).GetProperty("code").GetString());
        Assert.Equal("Order.NotFound", (await ProblemAsync(unknown)).GetProperty("code").GetString());
        Assert.Equal("Store.NotFound", (await ProblemAsync(viaTheirStore)).GetProperty("code").GetString());
        Assert.Equal("Verified", await StatusInDatabaseAsync(theirOrder.Id));
    }

    [Fact]
    public async Task TwoStoreUsersShippingTheSameOrderAtOnce_OneWins_AndTheOtherIsToldNo()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var (second, _, _) = await StoreUserOfAsync(client, shop.Admin, shop.StoreId);
        var order = await OrderInAsync(client, shop, "Verified");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => ShipAsync(client, i % 2 == 0 ? shop.StoreUser : second, shop.StoreId, order.Id))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Contains((await ProblemAsync(loser)).GetProperty("code").GetString(), LosersCodes, StringComparer.Ordinal);
        }

        Assert.Equal("Shipped", await StatusInDatabaseAsync(order.Id));
    }
}
