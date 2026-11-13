using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>The inventory list with <c>?lowStock=true</c>, against the real API and PostgreSQL: the whole path from setting a level to seeing what has fallen to it.</summary>
public class LowStockListEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Shop(string Admin, Guid StoreId, string StoreUser);

    private static async Task<Shop> AShopAsync(HttpClient client)
    {
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        return new Shop(admin, storeId, token);
    }

    private static async Task<(Guid Id, string Name)> ProductAsync(HttpClient client, Shop shop, int stock, int? level)
    {
        var product = await CreateProductAsync(client, shop.StoreUser, shop.StoreId);
        if (stock > 0)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{product.Id}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(stock))).StatusCode);
        }

        if (level is not null)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/stores/{shop.StoreId}/products/{product.Id}/low-stock-threshold", shop.StoreUser, new SetLowStockThresholdRequest(level))).StatusCode);
        }

        return (product.Id, product.Name);
    }

    private static async Task<PagedResponse<InventoryEntryResponse>> LowAsync(HttpClient client, string token, Guid storeId, string query = "?lowStock=true")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{storeId}/inventory{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<InventoryEntryResponse>>())!;
    }

    [Fact]
    public async Task TheLowStockList_HoldsOnlyWhatHasFallenToItsLevel_AtTheBoundaryIncluded()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var below = await ProductAsync(client, shop, 4, 5);
        var at = await ProductAsync(client, shop, 5, 5);
        await ProductAsync(client, shop, 6, 5); // above its level
        await ProductAsync(client, shop, 0, null); // no level: never low, even with nothing on hand
        await ProductAsync(client, shop, 50, null);

        var low = await LowAsync(client, shop.StoreUser, shop.StoreId);

        Assert.Equal(new[] { below.Id, at.Id }.Order(), low.Items.Select(entry => entry.ProductId).Order());
        Assert.All(low.Items, entry => Assert.True(entry.IsLow));
        Assert.Equal(2, low.TotalCount);
    }

    [Fact]
    public async Task AProductLeavesTheList_WhenStockIsAdded_AndComesBack_WhenItIsSold()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var milk = await ProductAsync(client, shop, 3, 5);
        Assert.Equal([milk.Id], (await LowAsync(client, shop.StoreUser, shop.StoreId)).Items.Select(entry => entry.ProductId));

        await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{milk.Id}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(10)); // 13
        Assert.Empty((await LowAsync(client, shop.StoreUser, shop.StoreId)).Items);

        await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{milk.Id}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(-8)); // 5: at the level
        Assert.Equal([milk.Id], (await LowAsync(client, shop.StoreUser, shop.StoreId)).Items.Select(entry => entry.ProductId));
    }

    [Fact]
    public async Task AnOrderThatTakesTheStockToTheLevel_PutsTheProductOnTheList()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var milk = await ProductAsync(client, shop, 10, 8);
        var customer = (await RegisterAsync(client)).Token;
        Assert.Empty((await LowAsync(client, shop.StoreUser, shop.StoreId)).Items);

        var order = await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders", customer, new GrocerEase.Contracts.Orders.PlaceOrderRequest("1 High Street", [new GrocerEase.Contracts.Orders.OrderLineRequest(milk.Id, 2)]));
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);

        var low = await LowAsync(client, shop.StoreUser, shop.StoreId);
        Assert.Equal([(milk.Id, 8, 8)], low.Items.Select(entry => (entry.ProductId, entry.QuantityOnHand, entry.LowStockThreshold!.Value)));
    }

    [Fact]
    public async Task AnArchivedProduct_NeverAppears()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var old = await ProductAsync(client, shop, 1, 5);
        var live = await ProductAsync(client, shop, 1, 5);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{old.Id}", shop.StoreUser)).StatusCode);

        var low = await LowAsync(client, shop.StoreUser, shop.StoreId);

        Assert.Equal([live.Id], low.Items.Select(entry => entry.ProductId));
        Assert.Equal(1, low.TotalCount);
    }

    [Fact]
    public async Task EachStoreSeesOnlyItsOwnLowStock_AndAStoreThatIsNotTheirsIsNotFound()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);
        var myMilk = await ProductAsync(client, mine, 1, 5);
        var theirMilk = await ProductAsync(client, theirs, 1, 5);

        Assert.Equal([myMilk.Id], (await LowAsync(client, mine.StoreUser, mine.StoreId)).Items.Select(entry => entry.ProductId));
        Assert.Equal([theirMilk.Id], (await LowAsync(client, theirs.StoreUser, theirs.StoreId)).Items.Select(entry => entry.ProductId));
        var notMine = await SendAsync(client, HttpMethod.Get, $"/stores/{theirs.StoreId}/inventory?lowStock=true", mine.StoreUser);
        var unknown = await SendAsync(client, HttpMethod.Get, $"/stores/{Guid.CreateVersion7()}/inventory?lowStock=true", mine.StoreUser);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (notMine.StatusCode, unknown.StatusCode));
        Assert.Equal("Store.NotFound", (await ProblemAsync(notMine)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TheLowStockList_IsPaged_AndWithoutTheFlagEveryProductIsListed()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        for (var i = 0; i < 3; i++)
        {
            await ProductAsync(client, shop, 1, 5);
        }

        await ProductAsync(client, shop, 100, 5); // fine

        var page = await LowAsync(client, shop.StoreUser, shop.StoreId, "?lowStock=true&page=2&pageSize=2");
        var everything = await LowAsync(client, shop.StoreUser, shop.StoreId, "");
        var explicitFalse = await LowAsync(client, shop.StoreUser, shop.StoreId, "?lowStock=false");

        Assert.Equal((1, 2, 3), (page.Items.Count, page.Page, page.TotalCount));
        Assert.Equal((4, 4), (everything.TotalCount, explicitFalse.TotalCount));
    }

    [Fact]
    public async Task OnlyAStoreUserSeesIt_ACustomerOrAnAdminIs403_AndNoTokenIs401()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory?lowStock=true", customer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory?lowStock=true", shop.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory?lowStock=true", null)).StatusCode);
    }

    [Fact]
    public async Task AFlagThatIsNotABoolean_Is400_NotA500()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory?lowStock=banana", shop.StoreUser);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("lowStock", out _));
    }
}
