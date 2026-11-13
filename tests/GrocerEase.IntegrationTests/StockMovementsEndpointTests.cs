using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Reading the history of a product's stock, against the real API and PostgreSQL. <c>inventory.manage</c> says what kind of user may; only a
/// membership says in which store, and a store that is not yours is a <c>404</c>, identical to one that does not exist.
/// </summary>
public class StockMovementsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<PagedResponse<StockMovementResponse>> HistoryAsync(HttpClient client, string token, Guid storeId, Guid productId, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/products/{productId}/stock-movements{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<StockMovementResponse>>())!;
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/products/{Guid.CreateVersion7()}/stock-movements", null)).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserReadsTheHistory_ACustomerOrAnAdminIs403(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : (await RegisterAsync(client)).Token;

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-movements", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheHistory_IsNewestFirst_AndSaysByHowMuchWhyWhoAndWhichOrder_AndAddsUpToTheQuantityTheInventoryShows()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 0); // one delivery of 10 milk
        var customer = await RegisterAsync(client);
        await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-adjustments", shop.StoreUser, new { delta = -2, reason = "Correction" });
        var order = await CreatedAsync(await PlaceAsync(client, customer.Token, shop.StoreId, Order(lines: (shop.Milk.Id, 3))));
        await SendAsync(client, HttpMethod.Post, $"/orders/{order.Id}/cancel", customer.Token);

        var history = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id);

        Assert.Equal(
            [("OrderCancelled", 3, (Guid?)customer.Id, (Guid?)order.Id), ("OrderPlaced", -3, customer.Id, order.Id), ("Correction", -2, shop.StoreUserId, null), ("Received", 10, shop.StoreUserId, null)],
            history.Items.Select(movement => (movement.Reason, movement.Delta, movement.ActorId, movement.OrderId)));
        Assert.Equal(4, history.TotalCount);
        Assert.All(history.Items, movement => Assert.Equal(shop.Milk.Id, movement.ProductId));
        Assert.Equal(history.Items.OrderByDescending(movement => movement.CreatedAt).Select(movement => movement.Id), history.Items.Select(movement => movement.Id)); // by time, newest first
        // The history adds up to the stock the inventory list shows for the same product.
        var inventory = await (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory", shop.StoreUser)).Content.ReadFromJsonAsync<PagedResponse<InventoryEntryResponse>>();
        Assert.Equal(inventory!.Items.Single(entry => entry.ProductId == shop.Milk.Id).QuantityOnHand, history.Items.Sum(movement => movement.Delta));
    }

    [Fact]
    public async Task TheHistory_IsPaged_AndEveryPageTogetherAddsUpToTheQuantity()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 0, breadStock: 0);
        foreach (var delta in new[] { 10, -1, 5, -2, 3 })
        {
            await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(delta));
        }

        var first = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, "?page=1&pageSize=2");
        var second = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, "?page=2&pageSize=2");
        var third = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, "?page=3&pageSize=2");
        var past = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, "?page=9&pageSize=2");

        Assert.Equal([3, -2], first.Items.Select(movement => movement.Delta)); // newest first
        Assert.Equal([5, -1], second.Items.Select(movement => movement.Delta));
        Assert.Equal([10], third.Items.Select(movement => movement.Delta));
        Assert.Equal((5, 5, 5), (first.TotalCount, second.TotalCount, third.TotalCount));
        Assert.Empty(past.Items);
        Assert.Equal(15, new[] { first, second, third }.SelectMany(page => page.Items).Sum(movement => movement.Delta));
        Assert.Equal(15, await StockAsync(shop.Milk.Id)); // 10 - 1 + 5 - 2 + 3
    }

    [Fact]
    public async Task AnotherProductsMovements_AreNeverInIt()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 4, breadStock: 7);

        var milk = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id);
        var bread = await HistoryAsync(client, shop.StoreUser, shop.StoreId, shop.Bread.Id);

        Assert.Equal([4], milk.Items.Select(movement => movement.Delta));
        Assert.Equal([7], bread.Items.Select(movement => movement.Delta));
    }

    [Fact]
    public async Task AStoreThatIsNotTheirs_AnUnknownStore_AndAProductOfAnotherStore_AreNotFound()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);

        var theirStore = await SendAsync(client, HttpMethod.Get, $"/stores/{theirs.StoreId}/products/{theirs.Milk.Id}/stock-movements", mine.StoreUser);
        var unknownStore = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/products/{mine.Milk.Id}/stock-movements", mine.StoreUser);
        var theirProductViaMyStore = await SendAsync(client, HttpMethod.Get, $"/stores/{mine.StoreId}/products/{theirs.Milk.Id}/stock-movements", mine.StoreUser);
        var unknownProduct = await SendAsync(client, HttpMethod.Get, $"/stores/{mine.StoreId}/products/{Guid.CreateVersion7()}/stock-movements", mine.StoreUser);

        Assert.Equal("Store.NotFound", (await ProblemAsync(theirStore)).GetProperty("code").GetString());
        Assert.Equal("Store.NotFound", (await ProblemAsync(unknownStore)).GetProperty("code").GetString());
        Assert.Equal("Product.NotFound", (await ProblemAsync(theirProductViaMyStore)).GetProperty("code").GetString());
        Assert.Equal("Product.NotFound", (await ProblemAsync(unknownProduct)).GetProperty("code").GetString());
        Assert.All(new[] { theirStore, unknownStore, theirProductViaMyStore, unknownProduct }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [Fact]
    public async Task AnArchivedProduct_IsNotFound_LikeInTheCatalogue()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}", shop.StoreUser)).StatusCode);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-movements", shop.StoreUser);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    public async Task ABadPage_Is400_NamingTheField(string query, string parameter)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-movements?{query}", shop.StoreUser);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }
}
