using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Orders;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A customer cancelling their own order, against the real API and PostgreSQL. Only the owner, and only while the order is pending: once the
/// store has verified it, only the store can cancel. When the customer and the store act at the same moment there is exactly one winner, and
/// the stock goes back only if the cancellation won.
/// </summary>
public class CancelMyOrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] LosersCodes = ["Order.InvalidTransition", "Order.ChangedMeanwhile"];

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, string? token, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/orders/{orderId}/cancel", token);

    private static async Task<OrderResponse> PlacedAsync(HttpClient client, Shop shop, string customer, int milk = 2) =>
        await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, milk))));

    private static async Task<string?> StatusInDatabaseAsync(Guid orderId) =>
        await PostgresServer.ScalarAsync<string?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select status from orders where id = '{orderId}'");

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(client, null, Guid.CreateVersion7())).StatusCode);
    }

    [Theory]
    [InlineData("storeuser")]
    [InlineData("admin")]
    public async Task OnlyACustomerCancelsHereOrders_AStoreUserOrAnAdminIs403_AndNothingMoves(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var order = await PlacedAsync(client, shop, (await RegisterAsync(client)).Token, 2);

        var response = await CancelAsync(client, string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : shop.StoreUser, order.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(("Pending", 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Fact]
    public async Task TheOwner_CancelsAPendingOrder_AndTheStockGoesBack_AndEveryoneSeesItCancelled()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer, 3); // 7 left

        var response = await CancelAsync(client, customer, order.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((order.Id, "Cancelled", order.Lines.Count), (cancelled.Id, cancelled.Status, cancelled.Lines.Count));
        Assert.Equal(("Cancelled", 10), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
        Assert.Equal("Cancelled", (await (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", customer)).Content.ReadFromJsonAsync<OrderResponse>())!.Status);
        var seenByStore = await (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/orders?status=Cancelled", shop.StoreUser)).Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>();
        Assert.Equal([order.Id], seenByStore!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task OnceTheStoreHasVerifiedIt_TheCustomerCannotCancel_409_AndNoStockMoves_ButTheStoreStillCan()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer, 2); // 8 left
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{order.Id}/verify", shop.StoreUser)).StatusCode);

        var response = await CancelAsync(client, customer, order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("Order.InvalidTransition", problem.GetProperty("code").GetString());
        Assert.Contains("Verified", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(("Verified", 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
        // The store's own cancellation is a different permission and a different rule: it still works on a verified order.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{order.Id}/cancel", shop.StoreUser)).StatusCode);
        Assert.Equal(("Cancelled", 10), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task AShippedOrAlreadyCancelledOrder_Is409_AndNoStockMoves(string status)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer, 2);
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = '{status}' where id = '{order.Id}'");

        var response = await CancelAsync(client, customer, order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((status, 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Fact]
    public async Task SomeoneElsesOrder_AndAnUnknownOne_AreTheSame404_AndNothingMoves()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var order = await PlacedAsync(client, shop, (await RegisterAsync(client)).Token, 2);
        var stranger = (await RegisterAsync(client)).Token;

        var theirs = await CancelAsync(client, stranger, order.Id);
        var unknown = await CancelAsync(client, stranger, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (a, b) = (await ProblemAsync(theirs), await ProblemAsync(unknown));
        Assert.Equal((a.GetProperty("code").GetString(), a.GetProperty("detail").GetString()), (b.GetProperty("code").GetString(), b.GetProperty("detail").GetString()));
        Assert.Equal("Order.NotFound", a.GetProperty("code").GetString());
        Assert.Equal(("Pending", 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Fact]
    public async Task TenCancellationsOfTheSameOrderAtOnce_OneWins_AndTheStockGoesBackOnce()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer, 4); // 6 left

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => CancelAsync(client, customer, order.Id))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Contains((await ProblemAsync(loser)).GetProperty("code").GetString(), LosersCodes, StringComparer.Ordinal);
        }

        Assert.Equal(("Cancelled", 10), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id))); // 6 + 4, not 6 + 40
        await Ledger.AgreesAsync(shop.StoreId); // however the stock moved, the ledger adds up to it
    }

    // The case that would double-restore, or restore for a verified order, if the guard were a read followed by a write.
    [Fact]
    public async Task TheCustomerCancellingWhileTheStoreVerifies_HasExactlyOneWinner_AndTheStockGoesBackOnlyIfTheCustomerWon()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 100);
        var customer = (await RegisterAsync(client)).Token;
        var orders = new List<OrderResponse>();
        for (var i = 0; i < 16; i++)
        {
            orders.Add(await PlacedAsync(client, shop, customer, 2)); // 100 - 32 = 68 left
        }

        var results = await Task.WhenAll(orders.Select(async order =>
        {
            var both = await Task.WhenAll(
                Task.Run(() => CancelAsync(client, customer, order.Id)),
                Task.Run(() => SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{order.Id}/verify", shop.StoreUser)));
            return (order.Id, Cancel: both[0].StatusCode, Verify: both[1].StatusCode);
        }));

        foreach (var (id, cancel, verify) in results)
        {
            Assert.Equal(1, new[] { cancel, verify }.Count(status => status == HttpStatusCode.OK)); // exactly one of the two won
            Assert.Equal(cancel == HttpStatusCode.OK ? "Cancelled" : "Verified", await StatusInDatabaseAsync(id));
        }

        // Each cancelled order put its 2 back; a verified one did not.
        Assert.Equal(68 + (2 * results.Count(result => result.Cancel == HttpStatusCode.OK)), await StockAsync(shop.Milk.Id));
        await Ledger.AgreesAsync(shop.StoreId); // however the stock moved, the ledger adds up to it
    }
}
