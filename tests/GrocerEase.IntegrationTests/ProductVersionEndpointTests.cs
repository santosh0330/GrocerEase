using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Products;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Two people editing the same product, against the real API and PostgreSQL. Nothing is locked while anyone edits: each sends back the
/// version they read, the first to save wins, and the second is refused with the product as it is now, instead of silently overwriting it.
/// </summary>
public class ProductVersionEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Shop(string Admin, Guid StoreId, string StoreUser, ProductResponse Milk);

    private static async Task<Shop> AShopAsync(HttpClient client)
    {
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        return new Shop(admin, storeId, token, await CreateProductAsync(client, token, storeId));
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, string token, Guid storeId, Guid productId, object body) =>
        SendAsync(client, HttpMethod.Put, $"/stores/{storeId}/products/{productId}", token, body);

    private static object Edit(string name, int? version, decimal price = 3.20m) => new { name, description = $"by {name}", price, version };

    private static async Task<ProductResponse> ReadAsync(HttpClient client, Shop shop) =>
        (await (await SendAsync(client, HttpMethod.Get, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}", shop.StoreUser)).Content.ReadFromJsonAsync<ProductResponse>())!;

    [Fact]
    public async Task TwoEditsFromTheSameVersion_TheFirstWins_TheSecondIs409_AndNothingOfTheSecondIsApplied()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var v1 = shop.Milk.Version; // both people read version 1

        var first = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("First", v1, 4m));
        var second = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Second", v1, 9m));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await first.Content.ReadFromJsonAsync<ProductResponse>())!.Version);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var stored = await ReadAsync(client, shop);
        Assert.Equal(("First", "by First", 4m, 2), (stored.Name, stored.Description, stored.Price, stored.Version)); // the second changed nothing, not even the price
    }

    [Fact]
    public async Task TheRefusal_IsProductOutdated_AndCarriesTheProductAsItIsNow_SoTheClientCanShowTheDifference()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("First", shop.Milk.Version, 4m));

        var refused = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Second", shop.Milk.Version, 9m));

        var problem = await ProblemAsync(refused);
        Assert.Equal(("Product.Outdated", 409), (problem.GetProperty("code").GetString(), problem.GetProperty("status").GetInt32()));
        var current = problem.GetProperty("current");
        Assert.Equal(
            (shop.Milk.Id, shop.StoreId, "First", "by First", 4m, 2),
            (current.GetProperty("id").GetGuid(), current.GetProperty("storeId").GetGuid(), current.GetProperty("name").GetString(), current.GetProperty("description").GetString(),
                current.GetProperty("price").GetDecimal(), current.GetProperty("version").GetInt32()));
    }

    [Fact]
    public async Task AfterTheRefusal_RetryingFromTheVersionInIt_Works()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("First", shop.Milk.Version, 4m));
        var refused = await ProblemAsync(await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Second", shop.Milk.Version, 9m)));
        var currentVersion = refused.GetProperty("current").GetProperty("version").GetInt32();

        var retry = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Second", currentVersion, 9m)); // on top of what they saw

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var stored = await ReadAsync(client, shop);
        Assert.Equal(("Second", 3), (stored.Name, stored.Version));
    }

    [Fact]
    public async Task AnEditFromTheVersionJustRead_AlwaysWorks_AndEachOneIsTheNextVersion()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var version = shop.Milk.Version;

        for (var i = 0; i < 5; i++)
        {
            var response = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit($"Name {i}", version));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            version = (await response.Content.ReadFromJsonAsync<ProductResponse>())!.Version;
        }

        Assert.Equal(6, version);
        Assert.Equal(6, (await ReadAsync(client, shop)).Version);
    }

    [Theory]
    [InlineData(null)] // no version sent at all
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AnEditWithNoVersion_OrNonsense_Is400_NamingTheField_AndIsNotAppliedBlindly(int? version)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var body = version is null ? (object)new { name = "Hacked", description = "x", price = 1m } : Edit("Hacked", version);

        var response = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty("version", out _));
        var stored = await ReadAsync(client, shop);
        Assert.Equal((shop.Milk.Name, 1), (stored.Name, stored.Version));
    }

    [Fact]
    public async Task AVersionThatIsNotANumber_Is400_NotA500()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, new { name = "x", description = "", price = 1m, version = "one" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AVersionFromTheFuture_IsRefusedToo()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var response = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Hacked", 99));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Product.Outdated", (await ProblemAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task TenPeopleEditingFromTheSameVersionAtOnce_OneWins_NineAreToldTheProductChanged_AndEachIsShownTheWinnersProduct()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            Task.Run(() => EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit($"Name {i}", shop.Milk.Version, 1m + i)))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var stored = await ReadAsync(client, shop);
        Assert.Equal(2, stored.Version); // written once
        Assert.Equal($"by {stored.Name}", stored.Description); // one whole edit, not a mixture
        foreach (var loser in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            var current = (await ProblemAsync(loser)).GetProperty("current");
            Assert.Equal((stored.Name, 2), (current.GetProperty("name").GetString(), current.GetProperty("version").GetInt32())); // the winner's product
        }
    }

    [Fact]
    public async Task AStaleEdit_OfAProductInAStoreThatIsNotTheirs_Is404_AndRevealsNothingOfTheProduct()
    {
        using var client = factory.CreateClient();
        var mine = await AShopAsync(client);
        var theirs = await AShopAsync(client);
        await EditAsync(client, theirs.StoreUser, theirs.StoreId, theirs.Milk.Id, Edit("Their edit", 1)); // their product is now version 2

        var viaTheirStore = await EditAsync(client, mine.StoreUser, theirs.StoreId, theirs.Milk.Id, Edit("Hacked", 1)); // a stale version, in their store
        var viaMyStore = await EditAsync(client, mine.StoreUser, mine.StoreId, theirs.Milk.Id, Edit("Hacked", 1)); // their product through my store

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (viaTheirStore.StatusCode, viaMyStore.StatusCode));
        foreach (var response in new[] { viaTheirStore, viaMyStore })
        {
            var problem = await ProblemAsync(response);
            Assert.False(problem.TryGetProperty("current", out _)); // a 409 would have shown them the product; a 404 shows nothing
            Assert.NotEqual("Product.Outdated", problem.GetProperty("code").GetString(), StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task AnArchivedProduct_IsNotFound_WhateverVersionIsSent_NotOutdated()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}", shop.StoreUser)).StatusCode);

        var stale = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Back", 1));
        var current = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit("Back", 2));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (stale.StatusCode, current.StatusCode));
        Assert.Equal("Product.NotFound", (await ProblemAsync(stale)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task ARenameToANameThatIsTaken_WithTheRightVersion_IsStillTheDuplicateNameConflict_AndKeepsTheVersion()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var bread = await CreateProductAsync(client, shop.StoreUser, shop.StoreId);

        var response = await EditAsync(client, shop.StoreUser, shop.StoreId, shop.Milk.Id, Edit(bread.Name, shop.Milk.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Product.DuplicateName", (await ProblemAsync(response)).GetProperty("code").GetString());
        Assert.Equal(1, (await ReadAsync(client, shop)).Version); // a refused edit is not a change
    }

    [Fact]
    public async Task OnlyAStoreUserEdits_ACustomerOrAnAdminIs403_AndTheVersionIsUntouched()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;

        var asCustomer = await EditAsync(client, customer, shop.StoreId, shop.Milk.Id, Edit("Hacked", 1));
        var asAdmin = await EditAsync(client, shop.Admin, shop.StoreId, shop.Milk.Id, Edit("Hacked", 1));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (asCustomer.StatusCode, asAdmin.StatusCode));
        Assert.Equal(1, (await ReadAsync(client, shop)).Version);
    }
}
