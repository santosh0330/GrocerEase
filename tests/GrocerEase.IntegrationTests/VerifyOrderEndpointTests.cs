using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Orders;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// A store user verifying a pending order, against the real API and PostgreSQL. <c>orders.manage</c> says what kind of user may; the
/// membership says in which store; the entity says from which status; the database says who wins when two try at once.
/// </summary>
public class VerifyOrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] LosersCodes = ["Order.InvalidTransition", "Order.ChangedMeanwhile"];

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string? token, Guid storeId, Guid orderId) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/orders/{orderId}/verify", token);

    private static async Task<OrderResponse> PlacedAsync(HttpClient client, Shop shop, string? customer = null) =>
        await CreatedAsync(await PlaceAsync(client, customer ?? (await RegisterAsync(client)).Token, shop.StoreId, Order(lines: (shop.Milk.Id, 2))));

    private static async Task<string?> StatusInDatabaseAsync(Guid orderId) =>
        await PostgresServer.ScalarAsync<string?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select status from orders where id = '{orderId}'");

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await VerifyAsync(client, null, Guid.CreateVersion7(), Guid.CreateVersion7())).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserVerifies_ACustomerOrAnAdminIs403_AndTheOrderStaysPending(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer);

        var response = await VerifyAsync(client, string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : customer, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Pending", await StatusInDatabaseAsync(order.Id));
    }

    [Fact]
    public async Task TheStoreUser_VerifiesAPendingOrder_AndItIsVerifiedForEveryoneWhoReadsIt()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var order = await PlacedAsync(client, shop, customer);

        var response = await VerifyAsync(client, shop.StoreUser, shop.StoreId, order.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verified = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((order.Id, "Verified", order.Total, order.Lines.Count), (verified.Id, verified.Status, verified.Total, verified.Lines.Count));
        Assert.Equal("Verified", await StatusInDatabaseAsync(order.Id));
        var mine = (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", customer)); // the customer sees it, too
        Assert.Equal("Verified", (await mine.Content.ReadFromJsonAsync<OrderResponse>())!.Status);
    }

    [Theory]
    [InlineData("Verified")]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task AnOrderThatIsNotPending_Is409_NamingItsStatus_AndIsNotChanged(string status)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var order = await PlacedAsync(client, shop);
        await PostgresServer.ExecuteAsync(await PostgresServer.ApplicationConnectionStringAsync(), $"update orders set status = '{status}' where id = '{order.Id}'");

        var response = await VerifyAsync(client, shop.StoreUser, shop.StoreId, order.Id);

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
        var theirOrder = await PlacedAsync(client, theirs);

        var viaMyStore = await VerifyAsync(client, mine.StoreUser, mine.StoreId, theirOrder.Id); // their order, my route
        var viaTheirStore = await VerifyAsync(client, mine.StoreUser, theirs.StoreId, theirOrder.Id); // their order, their route
        var unknown = await VerifyAsync(client, mine.StoreUser, mine.StoreId, Guid.CreateVersion7());

        Assert.All(new[] { viaMyStore, viaTheirStore, unknown }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal("Order.NotFound", (await ProblemAsync(viaMyStore)).GetProperty("code").GetString());
        Assert.Equal("Order.NotFound", (await ProblemAsync(unknown)).GetProperty("code").GetString());
        Assert.Equal("Store.NotFound", (await ProblemAsync(viaTheirStore)).GetProperty("code").GetString()); // not a member of that store at all
        Assert.Equal("Pending", await StatusInDatabaseAsync(theirOrder.Id));
    }

    [Fact]
    public async Task TwoStoreUsersVerifyingTheSameOrderAtOnce_OneWins_AndTheOtherIsToldNo()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var (second, _, _) = await StoreUserOfAsync(client, shop.Admin, shop.StoreId);
        var order = await PlacedAsync(client, shop);

        // Several requests from each of two people, all started together.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => VerifyAsync(client, i % 2 == 0 ? shop.StoreUser : second, shop.StoreId, order.Id))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        foreach (var loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            // Either it read the order after the winner (already Verified) or before and lost the write.
            Assert.Contains((await ProblemAsync(loser)).GetProperty("code").GetString(), LosersCodes, StringComparer.Ordinal);
        }

        Assert.Equal("Verified", await StatusInDatabaseAsync(order.Id));
    }
}
