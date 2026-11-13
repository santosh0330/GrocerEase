using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Orders;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A store user cancelling an order that has not shipped, against the real API and PostgreSQL. The stock the order reserved goes back, once:
/// however many cancel at once, and never for an order that shipped.
/// </summary>
public class CancelOrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] LosersCodes = ["Order.InvalidTransition", "Order.ChangedMeanwhile"];

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, string? token, Guid storeId, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/orders/{orderId}/cancel", token);

    private static Task<HttpResponseMessage> ShipAsync(HttpClient client, string? token, Guid storeId, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/orders/{orderId}/ship", token);

    // Placed through the API (so the stock really is reserved) and then put in the status under test directly in the database.
    private static async Task<OrderResponse> OrderInAsync(HttpClient client, Shop shop, string status, int milk = 2, string? customer = null)
    {
        var order = await CreatedAsync(await PlaceAsync(client, customer ?? (await RegisterAsync(client)).Token, shop.StoreId, Order(lines: (shop.Milk.Id, milk))));
        if (!string.Equals(status, "Pending", StringComparison.Ordinal))
        {
            await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = '{status}' where id = '{order.Id}'");
        }

        return order;
    }

    private static async Task<string?> StatusInDatabaseAsync(Guid orderId) =>
        await PostgresServer.ScalarAsync<string?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select status from orders where id = '{orderId}'");

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(client, null, Guid.CreateVersion7(), Guid.CreateVersion7())).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserCancels_ACustomerOrAnAdminIs403_AndNothingMoves(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await OrderInAsync(client, shop, "Pending", 2, customer);

        var response = await CancelAsync(client, string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : customer, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(("Pending", 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Verified")]
    public async Task TheStoreUser_CancelsAnOrderThatHasNotShipped_AndItsStockGoesBack(string status)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var order = await OrderInAsync(client, shop, status, 3, customer); // 10 - 3 = 7 left
        Assert.Equal(7, await StockAsync(shop.Milk.Id));

        var response = await CancelAsync(client, shop.StoreUser, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancelled = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((order.Id, "Cancelled", order.Lines.Count), (cancelled.Id, cancelled.Status, cancelled.Lines.Count));
        Assert.Equal(("Cancelled", 10), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
        Assert.Equal("Cancelled", (await (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", customer)).Content.ReadFromJsonAsync<OrderResponse>())!.Status);
    }

    [Fact]
    public async Task EveryLinesStockGoesBack_AndOnlyThatOrdersShare()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 10);
        var customer = (await RegisterAsync(client)).Token;
        var big = await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: [(shop.Milk.Id, 4), (shop.Bread.Id, 3)])));
        await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 1)))); // another order, which stays
        Assert.Equal((5, 7), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));

        await CancelAsync(client, shop.StoreUser, shop.StoreId, big.Id);

        Assert.Equal((9, 10), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id))); // 5 + 4, 7 + 3: the other order's 1 stays taken
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task AnOrderThatShippedOrIsAlreadyCancelled_Is409_NamingItsStatus_AndNoStockMoves(string status)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var order = await OrderInAsync(client, shop, status, 2); // 8 left

        var response = await CancelAsync(client, shop.StoreUser, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("Order.InvalidTransition", problem.GetProperty("code").GetString());
        Assert.Contains(status, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal((status, 8), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id)));
    }

    [Fact]
    public async Task AnOrderOfAnotherStore_AndAnUnknownOne_AreTheSame404_AndNoStockMoves()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client, milkStock: 10);
        var theirOrder = await OrderInAsync(client, theirs, "Pending", 2);

        var viaMyStore = await CancelAsync(client, mine.StoreUser, mine.StoreId, theirOrder.Id);
        var viaTheirStore = await CancelAsync(client, mine.StoreUser, theirs.StoreId, theirOrder.Id);
        var unknown = await CancelAsync(client, mine.StoreUser, mine.StoreId, Guid.CreateVersion7());

        Assert.All(new[] { viaMyStore, viaTheirStore, unknown }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal("Order.NotFound", (await ProblemAsync(viaMyStore)).GetProperty("code").GetString());
        Assert.Equal("Order.NotFound", (await ProblemAsync(unknown)).GetProperty("code").GetString());
        Assert.Equal("Store.NotFound", (await ProblemAsync(viaTheirStore)).GetProperty("code").GetString());
        Assert.Equal(("Pending", 8), (await StatusInDatabaseAsync(theirOrder.Id), await StockAsync(theirs.Milk.Id)));
    }

    [Fact]
    public async Task ManyCancellationsOfTheSameOrderAtOnce_OneWins_AndTheStockGoesBackOnce_NotManyTimes()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var (second, _, _) = await StoreUserOfAsync(client, shop.Admin, shop.StoreId);
        var order = await OrderInAsync(client, shop, "Pending", 4); // 6 left

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => CancelAsync(client, i % 2 == 0 ? shop.StoreUser : second, shop.StoreId, order.Id))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Contains((await ProblemAsync(loser)).GetProperty("code").GetString(), LosersCodes, StringComparer.Ordinal);
        }

        Assert.Equal(("Cancelled", 10), (await StatusInDatabaseAsync(order.Id), await StockAsync(shop.Milk.Id))); // 6 + 4, not 6 + 40
    }

    [Fact]
    public async Task ShippingAndCancellingTheSameOrderAtOnce_LeavesItEitherShippedWithItsStockGone_OrCancelledWithItsStockBack_NeverBoth()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 100);
        var orders = new List<OrderResponse>();
        for (var i = 0; i < 12; i++)
        {
            orders.Add(await OrderInAsync(client, shop, "Verified", 2)); // 100 - 24 = 76 left
        }

        // Each order gets a shipment and a cancellation started together.
        var results = await Task.WhenAll(orders.Select(async order =>
        {
            var both = await Task.WhenAll(Task.Run(() => ShipAsync(client, shop.StoreUser, shop.StoreId, order.Id)), Task.Run(() => CancelAsync(client, shop.StoreUser, shop.StoreId, order.Id)));
            return (order.Id, Ship: both[0].StatusCode, Cancel: both[1].StatusCode);
        }));

        foreach (var (id, ship, cancel) in results)
        {
            Assert.Equal(1, new[] { ship, cancel }.Count(status => status == HttpStatusCode.OK)); // exactly one of the two won
            Assert.Equal(ship == HttpStatusCode.OK ? "Shipped" : "Cancelled", await StatusInDatabaseAsync(id));
        }

        // The stock agrees with the winners: each cancelled order put its 2 back, each shipped one did not.
        Assert.Equal(76 + (2 * results.Count(result => result.Cancel == HttpStatusCode.OK)), await StockAsync(shop.Milk.Id));
    }

    // Thirty orders for the same two products, cancelled together: every one succeeds and every unit comes back. (That the two stock rows are
    // taken in a fixed order, so two cancellations cannot deadlock, is shown by CancelOrderDeadlockTests, which forces the timing.)
    [Fact]
    public async Task ManyOrdersForTheSameProducts_CancelledAtOnce_AllSucceed_AndAllTheStockComesBack()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 0, breadStock: 0);
        var customerId = (await RegisterAsync(client)).Id;
        var (first, second) = shop.Milk.Id.CompareTo(shop.Bread.Id) < 0 ? (shop.Milk, shop.Bread) : (shop.Bread, shop.Milk);
        var orderIds = new List<Guid>();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options))
        {
            for (var i = 0; i < 30; i++)
            {
                var id = Guid.CreateVersion7();
                var lines = i % 2 == 0 ? new[] { (first, 1), (second, 1) } : [(second, 1), (first, 1)];
                await new OrderRepository(db).AddAsync(
                    Domain.Entities.Order.Place(id, shop.StoreId, customerId, "1 High Street", DateTimeOffset.UtcNow, [.. lines.Select(line => new OrderLine(Guid.CreateVersion7(), id, line.Item1.Id, line.Item1.Name, 1m, line.Item2))]),
                    CancellationToken.None);
                orderIds.Add(id);
            }
        }

        var responses = await Task.WhenAll(orderIds.Select(id => Task.Run(() => CancelAsync(client, shop.StoreUser, shop.StoreId, id))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal((30, 30), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));
    }
}
