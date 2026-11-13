using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Common;
using GrocerEase.Contracts.Inventory;
using GrocerEase.Contracts.Orders;
using GrocerEase.Contracts.Products;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Placing an order and reading it, against the real API and PostgreSQL. Only a customer orders; the customer is the one in the token, never
/// the body; the stock of every line is reserved when the order is placed, all or nothing; and the last item goes to exactly one customer
/// however many ask at once.
/// </summary>
public class PlaceOrderEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task WithNoToken_Is401_ForPlacingAndForReading()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PlaceAsync(client, null, Guid.CreateVersion7(), Order())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"/orders/{Guid.CreateVersion7()}", null)).StatusCode);
    }

    [Theory]
    [InlineData("storeuser")]
    [InlineData("admin")]
    public async Task OnlyACustomerOrders_AStoreUserOrAnAdminIs403_AndNothingHappens(string who)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5);
        var token = string.Equals(who, "admin", StringComparison.Ordinal) ? shop.Admin : shop.StoreUser;

        var placed = await PlaceAsync(client, token, shop.StoreId, Order(lines: (shop.Milk.Id, 1)));
        var read = await SendAsync(client, HttpMethod.Get, $"/orders/{Guid.CreateVersion7()}", token);

        Assert.Equal(HttpStatusCode.Forbidden, placed.StatusCode);
        Assert.Equal("Authorization.Forbidden", (await ProblemAsync(placed)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(5, await StockAsync(shop.Milk.Id));
        Assert.Equal(0L, await OrdersInAsync(shop.StoreId));
    }

    [Fact]
    public async Task ACustomer_PlacesAnOrder_Pending_WithTheNamesAndPricesCopied_AndTheStockReserved()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 5);
        var customer = (await RegisterAsync(client)).Token;

        var response = await PlaceAsync(client, customer, shop.StoreId, Order("  1 High Street  ", (shop.Milk.Id, 2), (shop.Bread.Id, 1)));

        var order = await CreatedAsync(response);
        Assert.Equal((shop.StoreId, "Pending", "1 High Street", 6.10m), (order.StoreId, order.Status, order.DeliveryAddress, order.Total)); // trimmed
        Assert.Equal(
            new[] { (shop.Bread.Name, 1.10m, 1, 1.10m), (shop.Milk.Name, 2.50m, 2, 5.00m) }.OrderBy(line => line.Item1, StringComparer.Ordinal),
            order.Lines.Select(line => (line.ProductName, line.UnitPrice, line.Quantity, line.LineTotal)));
        Assert.Equal((8, 4), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));
        Assert.EndsWith($"/orders/{order.Id}", response.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal(1L, await OrdersInAsync(shop.StoreId));
    }

    [Fact]
    public async Task TheCustomer_IsTheOneInTheToken_NotTheOneTheBodyClaims()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var me = await RegisterAsync(client);
        var someoneElse = await RegisterAsync(client);

        // A body that names another customer: the field is not part of the contract, so it is ignored, and the order is mine.
        var response = await PlaceAsync(client, me.Token, shop.StoreId, new
        {
            customerId = someoneElse.Id,
            deliveryAddress = "x",
            lines = new[] { new { productId = shop.Milk.Id, quantity = 1 } },
        });

        var order = await CreatedAsync(response);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", me.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"/orders/{order.Id}", someoneElse.Token)).StatusCode);
    }

    [Fact]
    public async Task TheLocation_ReadsTheOrderBack_WithItsLines()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var placed = await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 3))));

        var read = await SendAsync(client, HttpMethod.Get, $"/orders/{placed.Id}", customer);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var order = (await read.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((placed.Id, placed.Total, placed.Status), (order.Id, order.Total, order.Status));
        Assert.Equal(placed.Lines, order.Lines);
    }

    [Fact]
    public async Task AnOrder_KeepsThePriceAndNameItWasBoughtAt_WhenTheProductChangesAndIsArchived()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var customer = (await RegisterAsync(client)).Token;
        var placed = await CreatedAsync(await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 2))));

        await SendAsync(client, HttpMethod.Put, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}", shop.StoreUser, new UpdateProductRequest("Renamed", "", 99m));
        await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}", shop.StoreUser);

        var read = (await (await SendAsync(client, HttpMethod.Get, $"/orders/{placed.Id}", customer)).Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal((shop.Milk.Name, 2.50m, 5.00m), (read.Lines[0].ProductName, read.Lines[0].UnitPrice, read.Total));
    }

    [Fact]
    public async Task NotEnoughStock_Is409_NamingTheProduct_AndChangesNothing()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 1);
        var customer = (await RegisterAsync(client)).Token;

        var response = await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("Order.InsufficientStock", problem.GetProperty("code").GetString());
        Assert.Contains(shop.Milk.Name, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, await StockAsync(shop.Milk.Id));
        Assert.Equal(0L, await OrdersInAsync(shop.StoreId));
    }

    [Fact]
    public async Task WhenOneLineCannotBeFilled_TheOtherLinesStockIsUntouched_AndThereIsNoOrder()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 1);
        var customer = (await RegisterAsync(client)).Token;

        var response = await PlaceAsync(client, customer, shop.StoreId, Order(lines: [(shop.Milk.Id, 3), (shop.Bread.Id, 2)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((10, 1, 0L), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id), await OrdersInAsync(shop.StoreId))); // not "7": it went back
    }

    [Fact]
    public async Task AProductThatHasNeverHadStock_CannotBeOrdered()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 0);
        var customer = (await RegisterAsync(client)).Token;

        var response = await PlaceAsync(client, customer, shop.StoreId, Order(lines: (shop.Milk.Id, 1)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(await StockAsync(shop.Milk.Id));
    }

    [Fact]
    public async Task TwentyCustomersOrderingOneEachAtTheSameTime_FromStockOfFive_FiveGetAnOrder_AndTheRestAreToldNoStock()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5);
        var customers = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ => (await RegisterAsync(client)).Token));

        var responses = await Task.WhenAll(customers.Select(token => PlaceAsync(client, token, shop.StoreId, Order(lines: (shop.Milk.Id, 1)))));

        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(15, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockAsync(shop.Milk.Id)); // never oversold
        Assert.Equal(5L, await OrdersInAsync(shop.StoreId)); // and one order for each unit that left
        await Ledger.AgreesAsync(shop.StoreId); // however the stock moved, the ledger adds up to it
    }

    [Fact]
    public async Task TheLastItem_GoesToExactlyOneOfTwoCustomers()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 1);
        var (first, second) = ((await RegisterAsync(client)).Token, (await RegisterAsync(client)).Token);

        var responses = await Task.WhenAll(
            PlaceAsync(client, first, shop.StoreId, Order(lines: (shop.Milk.Id, 1))),
            PlaceAsync(client, second, shop.StoreId, Order(lines: (shop.Milk.Id, 1))));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockAsync(shop.Milk.Id));
        await Ledger.AgreesAsync(shop.StoreId); // however the stock moved, the ledger adds up to it
    }

    [Fact]
    public async Task OrdersForTheSameProductsInOppositeOrders_NeverDeadlock()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 1000, breadStock: 1000);
        var customer = (await RegisterAsync(client)).Token;

        // Half ask for milk then bread, half for bread then milk. Without a fixed reservation order, two of these would each hold one row and
        // wait for the other, and PostgreSQL would kill one with a deadlock (a 500). With one, they simply queue.
        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => PlaceAsync(
            client, customer, shop.StoreId,
            i % 2 == 0 ? Order(lines: [(shop.Milk.Id, 1), (shop.Bread.Id, 1)]) : Order(lines: [(shop.Bread.Id, 1), (shop.Milk.Id, 1)]))));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal((970, 970), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));
        await Ledger.AgreesAsync(shop.StoreId); // however the stock moved, the ledger adds up to it
    }

    [Fact]
    public async Task AnUnknownStore_AnUnknownProduct_AnArchivedProduct_AndAnotherStoresProduct_Are404_AndNoStockMoves()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5);
        var otherShop = await AShopAsync(client, milkStock: 5);
        var archived = await CreateProductAsync(client, shop.StoreUser, shop.StoreId);
        await SetStockAsync(client, shop.StoreUser, shop.StoreId, archived.Id, 5);
        await SendAsync(client, HttpMethod.Delete, $"/stores/{shop.StoreId}/products/{archived.Id}", shop.StoreUser);
        var customer = (await RegisterAsync(client)).Token;

        var unknownStore = await PlaceAsync(client, customer, Guid.CreateVersion7(), Order(lines: (shop.Milk.Id, 1)));
        var unknownProduct = await PlaceAsync(client, customer, shop.StoreId, Order(lines: [(shop.Milk.Id, 1), (Guid.CreateVersion7(), 1)]));
        var archivedProduct = await PlaceAsync(client, customer, shop.StoreId, Order(lines: [(shop.Milk.Id, 1), (archived.Id, 1)]));
        var foreignProduct = await PlaceAsync(client, customer, shop.StoreId, Order(lines: [(shop.Milk.Id, 1), (otherShop.Milk.Id, 1)]));

        Assert.Equal(HttpStatusCode.NotFound, unknownStore.StatusCode);
        Assert.Equal("Store.NotFound", (await ProblemAsync(unknownStore)).GetProperty("code").GetString());
        foreach (var response in new[] { unknownProduct, archivedProduct, foreignProduct })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Product.NotFound", (await ProblemAsync(response)).GetProperty("code").GetString());
        }

        Assert.Equal((5, 5, 5), (await StockAsync(shop.Milk.Id), await StockAsync(archived.Id), await StockAsync(otherShop.Milk.Id)));
        Assert.Equal(0L, await OrdersInAsync(shop.StoreId));
    }

    [Fact]
    public async Task ABadOrder_Is400_NamingTheField_AndNothingHappens()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5);
        var customer = (await RegisterAsync(client)).Token;
        var milk = shop.Milk.Id;

        static string[] Keys(System.Text.Json.JsonElement errors) => [.. errors.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

        async Task<System.Text.Json.JsonElement> ErrorsOfAsync(object body)
        {
            var response = await PlaceAsync(client, customer, shop.StoreId, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await ProblemAsync(response)).GetProperty("errors");
        }

        Assert.True((await ErrorsOfAsync(Order(lines: []))).TryGetProperty("lines", out _));
        // Missing, null or malformed: MVC's own check catches these before the validator runs, yet the key is the one the client sent.
        Assert.Equal(["lines"], Keys(await ErrorsOfAsync(new { deliveryAddress = "x" })));
        Assert.Equal(["lines"], Keys(await ErrorsOfAsync(new { deliveryAddress = "x", lines = (object?)null })));
        Assert.Equal(["deliveryAddress"], Keys(await ErrorsOfAsync(new { lines = new[] { new { productId = milk, quantity = 1 } } })));
        Assert.All(Keys(await ErrorsOfAsync(new { deliveryAddress = "x", lines = new object?[] { null } })), key => Assert.StartsWith("lines", key, StringComparison.Ordinal));
        Assert.True((await ErrorsOfAsync(Order("", (milk, 1)))).TryGetProperty("deliveryAddress", out _));
        Assert.Equal(["lines[0].quantity"], Keys(await ErrorsOfAsync(Order(lines: (milk, 0)))));
        Assert.True((await ErrorsOfAsync(Order(lines: [(milk, 1), (milk, 2)]))).TryGetProperty("lines", out _)); // a product twice
        Assert.True((await ErrorsOfAsync(Order(lines: [.. Enumerable.Range(0, 101).Select(_ => (Guid.CreateVersion7(), 1))]))).TryGetProperty("lines", out _));
        Assert.Equal((5, 0L), (await StockAsync(milk), await OrdersInAsync(shop.StoreId)));
    }

    [Fact]
    public async Task AnOrder_IsReadOnlyByItsOwner_SomeoneElsesAndAnUnknownOneAreTheSame404()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var owner = (await RegisterAsync(client)).Token;
        var other = (await RegisterAsync(client)).Token;
        var placed = await CreatedAsync(await PlaceAsync(client, owner, shop.StoreId, Order(lines: (shop.Milk.Id, 1))));

        var theirs = await SendAsync(client, HttpMethod.Get, $"/orders/{placed.Id}", other);
        var unknown = await SendAsync(client, HttpMethod.Get, $"/orders/{Guid.CreateVersion7()}", other);

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (a, b) = (await ProblemAsync(theirs), await ProblemAsync(unknown));
        Assert.Equal((a.GetProperty("code").GetString(), a.GetProperty("detail").GetString()), (b.GetProperty("code").GetString(), b.GetProperty("detail").GetString()));
        Assert.Equal("Order.NotFound", a.GetProperty("code").GetString());
    }

    private static async Task<PagedResponse<OrderResponse>> MyOrdersAsync(HttpClient client, string token, string query = "")
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/orders{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<OrderResponse>>())!;
    }

    [Fact]
    public async Task ACustomer_ListsTheirOwnOrders_NewestFirst_WithTheirLines_AndNeverSomeoneElses()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var me = (await RegisterAsync(client)).Token;
        var other = (await RegisterAsync(client)).Token;
        var first = await CreatedAsync(await PlaceAsync(client, me, shop.StoreId, Order(lines: (shop.Milk.Id, 1))));
        await Task.Delay(20); // so the placing times differ
        var second = await CreatedAsync(await PlaceAsync(client, me, shop.StoreId, Order(lines: [(shop.Milk.Id, 2), (shop.Bread.Id, 1)])));
        await CreatedAsync(await PlaceAsync(client, other, shop.StoreId, Order(lines: (shop.Bread.Id, 1))));

        var mine = await MyOrdersAsync(client, me);
        var theirs = await MyOrdersAsync(client, other);

        Assert.Equal([second.Id, first.Id], mine.Items.Select(order => order.Id));
        Assert.Equal((2, 1, 2), (mine.TotalCount, mine.Items[1].Lines.Count, mine.Items[0].Lines.Count));
        Assert.Equal(1, theirs.TotalCount);
        Assert.DoesNotContain(theirs.Items, order => order.Id == first.Id || order.Id == second.Id);
    }

    [Fact]
    public async Task TheListOfOrders_IsPaged_AndAPagePastTheEndIsEmpty()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);
        var me = (await RegisterAsync(client)).Token;
        var placed = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            placed.Add((await CreatedAsync(await PlaceAsync(client, me, shop.StoreId, Order(lines: (shop.Milk.Id, 1))))).Id);
            await Task.Delay(20);
        }

        placed.Reverse(); // newest first
        var second = await MyOrdersAsync(client, me, "?page=2&pageSize=2");
        var past = await MyOrdersAsync(client, me, "?page=9");

        Assert.Equal([placed[2], placed[3]], second.Items.Select(order => order.Id));
        Assert.Equal((5, 2, 2), (second.TotalCount, second.Page, second.PageSize));
        Assert.Empty(past.Items);
        Assert.Equal(5, past.TotalCount);
    }

    [Fact]
    public async Task ACustomerWithNoOrders_ListsNone_AndTheListNeedsTheReadPermission()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client);

        var none = await MyOrdersAsync(client, (await RegisterAsync(client)).Token);

        Assert.Empty(none.Items);
        Assert.Equal(0, none.TotalCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/orders", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/orders", shop.StoreUser)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/orders", shop.Admin)).StatusCode);
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=101", "pageSize")]
    public async Task TheListOfOrders_PagingOutsideTheLimits_Is400_NamingTheParameter(string query, string parameter)
    {
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Get, $"/orders{query}", (await RegisterAsync(client)).Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ProblemAsync(response)).GetProperty("errors").TryGetProperty(parameter, out _));
    }
}
