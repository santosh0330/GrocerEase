using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Setting the low-stock level of a product, against the real API and PostgreSQL. <c>inventory.manage</c> says what kind of user may; only a
/// membership says in which store, and a store that is not yours is a <c>404</c>, identical to one that does not exist.
/// </summary>
public class LowStockThresholdEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Shop(string Admin, Guid StoreId, string StoreUser, Guid ProductId, string ProductName);

    private static async Task<Shop> AShopAsync(HttpClient client)
    {
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var product = await CreateProductAsync(client, token, storeId);
        return new Shop(admin, storeId, token, product.Id, product.Name);
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string? token, Guid storeId, Guid productId, object body) =>
        SendAsync(client, HttpMethod.Put, $"/stores/{storeId}/products/{productId}/low-stock-threshold", token, body);

    private static async Task<InventoryEntryResponse> EntryAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InventoryEntryResponse>())!;
    }

    private static async Task<(int? Quantity, int? Level)> RowAsync(Guid productId)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            await PostgresServer.ScalarAsync<int?>(connection, $"select quantity_on_hand from inventory_items where product_id = '{productId}'"),
            await PostgresServer.ScalarAsync<int?>(connection, $"select low_stock_threshold from inventory_items where product_id = '{productId}'"));
    }

    [Fact]
    public async Task WithNoToken_Is401()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await SetAsync(client, null, Guid.CreateVersion7(), Guid.CreateVersion7(), new { threshold = 5 })).StatusCode);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("admin")]
    public async Task OnlyAStoreUserSetsALevel_ACustomerOrAnAdminIs403_AndNothingIsWritten(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : (await RegisterAsync(client)).Token;

        var response = await SetAsync(client, token, shop.StoreId, shop.ProductId, new { threshold = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal((null, null), await RowAsync(shop.ProductId));
    }

    [Fact]
    public async Task TheStoreUser_SetsALevel_OnAProductThatHasNeverHadStock_WhichMakesItsRowAtZero_AndItIsLowAtOnce()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var entry = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 5 }));

        Assert.Equal(new InventoryEntryResponse(shop.ProductId, shop.ProductName, 0, 5, true), entry);
        Assert.Equal((0, 5), await RowAsync(shop.ProductId));
    }

    [Fact]
    public async Task TheLevel_DoesNotChangeTheQuantity_AndTheListShowsIt_WithTheBoundaryAtTheLevel()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{shop.ProductId}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(6))).StatusCode);

        var atSix = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 6 }));
        var atFive = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 5 }));

        Assert.Equal((6, 6, true), (atSix.QuantityOnHand, atSix.LowStockThreshold, atSix.IsLow)); // at the level counts as low
        Assert.Equal((6, 5, false), (atFive.QuantityOnHand, atFive.LowStockThreshold, atFive.IsLow)); // one above it does not
        var list = await (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/inventory", shop.StoreUser)).Content.ReadFromJsonAsync<PagedResponse<InventoryEntryResponse>>();
        Assert.Equal([new InventoryEntryResponse(shop.ProductId, shop.ProductName, 6, 5, false)], list!.Items);
    }

    [Fact]
    public async Task ANullLevel_ClearsIt_AndSettingTheSameLevelTwice_LeavesTheSameState()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var first = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 3 }));
        var again = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 3 }));
        var cleared = await EntryAsync(await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = (int?)null }));

        Assert.Equal(first, again); // a PUT: idempotent
        Assert.Equal((null, false), (cleared.LowStockThreshold, cleared.IsLow));
        Assert.Equal((0, null), await RowAsync(shop.ProductId));
    }

    [Fact]
    public async Task AStoreThatIsNotTheirs_AnUnknownStore_AndAProductOfAnotherStore_AreNotFound_AndNothingIsWritten()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);

        var theirStore = await SetAsync(client, mine.StoreUser, theirs.StoreId, theirs.ProductId, new { threshold = 5 });
        var unknownStore = await SetAsync(client, mine.StoreUser, Guid.CreateVersion7(), mine.ProductId, new { threshold = 5 });
        var theirProductViaMyStore = await SetAsync(client, mine.StoreUser, mine.StoreId, theirs.ProductId, new { threshold = 5 });
        var unknownProduct = await SetAsync(client, mine.StoreUser, mine.StoreId, Guid.CreateVersion7(), new { threshold = 5 });

        Assert.Equal("Store.NotFound", (await ProblemAsync(theirStore)).GetProperty("code").GetString());
        Assert.Equal("Store.NotFound", (await ProblemAsync(unknownStore)).GetProperty("code").GetString());
        Assert.Equal("Product.NotFound", (await ProblemAsync(theirProductViaMyStore)).GetProperty("code").GetString());
        Assert.Equal("Product.NotFound", (await ProblemAsync(unknownProduct)).GetProperty("code").GetString());
        Assert.All(new[] { theirStore, unknownStore, theirProductViaMyStore, unknownProduct }, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Equal((null, null), (await RowAsync(mine.ProductId)));
        Assert.Equal((null, null), (await RowAsync(theirs.ProductId)));
    }

    [Fact]
    public async Task AnArchivedProduct_IsNotFound()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{shop.ProductId}", shop.StoreUser)).StatusCode);

        var response = await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = 5 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Product.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    public async Task ALevelOutOfRange_Is400_NamingTheField_AndNothingIsWritten(int level)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = level });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("threshold", out _));
        Assert.Equal((null, null), await RowAsync(shop.ProductId));
    }

    [Fact]
    public async Task ALevelThatIsNotANumber_Is400_NotA500()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await SetAsync(client, shop.StoreUser, shop.StoreId, shop.ProductId, new { threshold = "five" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
