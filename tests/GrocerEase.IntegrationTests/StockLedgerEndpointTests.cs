using System.Net;
using GrocerEase.Contracts.Inventory;
using static GrocerEase.IntegrationTests.ApiScenario;
using static GrocerEase.IntegrationTests.OrderScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The stock ledger through the real API and PostgreSQL: whatever mix of adjustments, orders and cancellations touches a product, the sum of
/// its movements is its quantity, and each movement says who did it and for which order. If a writer ever skipped the ledger, this is the
/// test that would show it.
/// </summary>
public class StockLedgerEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Row(Guid ProductId, int Delta, string Reason, Guid? ActorId, Guid? OrderId);

    private static async Task<List<Row>> MovementsAsync(Guid storeId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(await PostgresServer.ApplicationConnectionStringAsync());
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("select product_id, delta, reason, actor_id, order_id from stock_movements where store_id = @s order by id", connection);
        command.Parameters.AddWithValue("s", storeId);
        var rows = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.IsDBNull(4) ? null : reader.GetGuid(4)));
        }

        return rows;
    }

    private static Task<HttpResponseMessage> AdjustAsync(HttpClient client, Shop shop, Guid productId, int delta) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{productId}/stock-adjustments", shop.StoreUser, new AdjustStockRequest(delta));

    [Fact]
    public async Task AMixOfAdjustmentsOrdersAndBothKindsOfCancellation_LeavesALedgerThatAddsUp_WithTheRightActorAndOrderOnEveryMovement()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 10); // two deliveries
        var alice = await RegisterAsync(client);
        var ben = await RegisterAsync(client);
        var cleo = await RegisterAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await AdjustAsync(client, shop, shop.Milk.Id, -2)).StatusCode); // a correction: 8 milk
        var first = await CreatedAsync(await PlaceAsync(client, alice.Token, shop.StoreId, Order(lines: [(shop.Milk.Id, 3), (shop.Bread.Id, 2)]))); // milk 5, bread 8
        var second = await CreatedAsync(await PlaceAsync(client, ben.Token, shop.StoreId, Order(lines: (shop.Milk.Id, 1)))); // milk 4
        var third = await CreatedAsync(await PlaceAsync(client, cleo.Token, shop.StoreId, Order(lines: (shop.Bread.Id, 4)))); // bread 4
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{first.Id}/cancel", shop.StoreUser)).StatusCode); // the store: milk 7, bread 6
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/orders/{second.Id}/cancel", ben.Token)).StatusCode); // the customer: milk 8
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{third.Id}/verify", shop.StoreUser)).StatusCode); // no stock moves
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{third.Id}/ship", shop.StoreUser)).StatusCode); // none here either

        Assert.Equal((8, 6), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));
        await Ledger.AgreesAsync(shop.StoreId);
        var movements = await MovementsAsync(shop.StoreId);
        var (milk, bread, store) = (shop.Milk.Id, shop.Bread.Id, shop.StoreUserId);
        (string Reason, Guid Product, int Delta, Guid? Actor, Guid? Order)[] expected =
        [
            ("Received", milk, 10, store, null), ("Received", bread, 10, store, null), // the two deliveries that made the shop
            ("Correction", milk, -2, store, null),
            ("OrderPlaced", milk, -3, alice.Id, first.Id), ("OrderPlaced", bread, -2, alice.Id, first.Id),
            ("OrderPlaced", milk, -1, ben.Id, second.Id),
            ("OrderPlaced", bread, -4, cleo.Id, third.Id),
            ("OrderCancelled", milk, 3, store, first.Id), ("OrderCancelled", bread, 2, store, first.Id), // cancelled by the store: its user is the actor
            ("OrderCancelled", milk, 1, ben.Id, second.Id), // withdrawn by the customer: the customer is the actor
        ];
        Assert.Equal(Lines(expected), Lines(movements.Select(row => (row.Reason, row.ProductId, row.Delta, row.ActorId, row.OrderId))));
    }

    // Order does not matter, only which movements exist: one comparable line each.
    private static List<string> Lines(IEnumerable<(string Reason, Guid Product, int Delta, Guid? Actor, Guid? Order)> rows) =>
        [.. rows.Select(row => $"{row.Reason}|{row.Product}|{row.Delta}|{row.Actor}|{row.Order}").Order(StringComparer.Ordinal)];

    [Fact]
    public async Task ShippingAndVerifying_MoveNoStock_SoTheyLeaveNoMovement()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10);
        var customer = await RegisterAsync(client);
        var order = await CreatedAsync(await PlaceAsync(client, customer.Token, shop.StoreId, Order(lines: (shop.Milk.Id, 2))));
        var before = await Ledger.CountAsync(shop.StoreId);

        await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{order.Id}/verify", shop.StoreUser);
        await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/orders/{order.Id}/ship", shop.StoreUser);

        Assert.Equal(before, await Ledger.CountAsync(shop.StoreId));
        await Ledger.AgreesAsync(shop.StoreId);
    }

    [Fact]
    public async Task ARefusedOrder_AndARefusedAdjustment_LeaveNoMovementAtAll()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5, breadStock: 1);
        var customer = await RegisterAsync(client);
        var before = await Ledger.CountAsync(shop.StoreId);

        // The milk line would be filled and the bread line cannot be: the whole order is refused, and the milk's movement goes back with its stock.
        var refusedOrder = await PlaceAsync(client, customer.Token, shop.StoreId, Order(lines: [(shop.Milk.Id, 2), (shop.Bread.Id, 2)]));
        var refusedAdjustment = await AdjustAsync(client, shop, shop.Milk.Id, -99);

        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.Conflict), (refusedOrder.StatusCode, refusedAdjustment.StatusCode));
        Assert.Equal(before, await Ledger.CountAsync(shop.StoreId));
        Assert.Equal((5, 1), (await StockAsync(shop.Milk.Id), await StockAsync(shop.Bread.Id)));
        await Ledger.AgreesAsync(shop.StoreId);
    }

    [Fact]
    public async Task TwentyCustomersOrderingTheLastFive_AtTheSameTime_LeaveFiveOrderPlacedMovements_AndALedgerThatAddsUpToZero()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 5);
        var customers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RegisterAsync(client)));

        var responses = await Task.WhenAll(customers.Select(customer => Task.Run(() => PlaceAsync(client, customer.Token, shop.StoreId, Order(lines: (shop.Milk.Id, 1))))));

        Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(0, await StockAsync(shop.Milk.Id));
        var placed = (await MovementsAsync(shop.StoreId)).Where(row => string.Equals(row.Reason, "OrderPlaced", StringComparison.Ordinal)).ToList();
        Assert.Equal((5, -5), (placed.Count, placed.Sum(row => row.Delta)));
        Assert.Equal(5, placed.Select(row => row.OrderId).Distinct().Count()); // each for a different order
        await Ledger.AgreesAsync(shop.StoreId);
    }

    private static async Task<List<string>> ReasonsOfAsync(Guid productId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(await PostgresServer.ApplicationConnectionStringAsync());
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("select reason || ':' || delta from stock_movements where product_id = @p order by id", connection);
        command.Parameters.AddWithValue("p", productId);
        var reasons = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            reasons.Add(reader.GetString(0));
        }

        return reasons;
    }

    [Fact]
    public async Task TheStoreUser_SaysWhy_AndTheLedgerRecordsIt_AndWithNoReasonTheSignDecides()
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 0, breadStock: 0);
        var milk = shop.Milk.Id;

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{milk}/stock-adjustments", shop.StoreUser, new { delta = 20, reason = "Received" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{milk}/stock-adjustments", shop.StoreUser, new { delta = -3, reason = "correction" })).StatusCode); // broken
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{milk}/stock-adjustments", shop.StoreUser, new { delta = 2, reason = "Correction" })).StatusCode); // the count was low
        Assert.Equal(HttpStatusCode.OK, (await AdjustAsync(client, shop, milk, 5)).StatusCode); // no reason: an addition is a delivery
        Assert.Equal(HttpStatusCode.OK, (await AdjustAsync(client, shop, milk, -1)).StatusCode); // and a removal is a correction

        Assert.Equal(["Received:20", "Correction:-3", "Correction:2", "Received:5", "Correction:-1"], await ReasonsOfAsync(milk));
        await Ledger.AgreesAsync(shop.StoreId);
    }

    [Theory]
    [InlineData(5, "OrderPlaced")]
    [InlineData(5, "OrderCancelled")]
    [InlineData(5, "OpeningBalance")]
    [InlineData(5, "Lost")]
    [InlineData(5, "1")]
    [InlineData(5, "")]
    [InlineData(-5, "Received")] // a delivery adds
    public async Task AReasonThatIsNotOneAPersonMayGive_OrDoesNotFit_Is400_NamingTheField_AndNothingChanges(int delta, string reason)
    {
        using var client = factory.CreateClient();
        var shop = await AShopAsync(client, milkStock: 10, breadStock: 0);
        var before = await Ledger.CountAsync(shop.StoreId);

        var response = await SendAsync(client, HttpMethod.Post, $"/stores/{shop.StoreId}/products/{shop.Milk.Id}/stock-adjustments", shop.StoreUser, new { delta, reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty(delta < 0 ? "delta" : "reason", out _));
        Assert.Equal((10, before), (await StockAsync(shop.Milk.Id), await Ledger.CountAsync(shop.StoreId)));
    }
}
