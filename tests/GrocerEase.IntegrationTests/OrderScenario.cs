using System.Net;
using System.Net.Http.Json;
using GrocerEase.Contracts.Inventory;
using GrocerEase.Contracts.Orders;
using GrocerEase.Contracts.Products;
using static GrocerEase.IntegrationTests.ApiScenario;

namespace GrocerEase.IntegrationTests;

/// <summary>What the order endpoint tests share: a store with two products and a store user, placing an order, and reading stock and order counts straight from the database.</summary>
public static class OrderScenario
{
    public sealed record Shop(string Admin, Guid StoreId, string StoreUser, ProductResponse Milk, ProductResponse Bread);

    /// <summary>A store with two products: milk (2.50) and bread (1.10), each with the stock given.</summary>
    public static async Task<Shop> AShopAsync(HttpClient client, int milkStock = 10, int breadStock = 10)
    {
        var admin = await AdminTokenAsync(client);
        var storeId = await CreateStoreAsync(client, admin);
        var (token, _, _) = await StoreUserOfAsync(client, admin, storeId);
        var milk = await CreateProductAsync(client, token, storeId, $"Milk {Guid.NewGuid():N}");
        var bread = await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products", token, new CreateProductRequest($"Bread {Guid.NewGuid():N}", "", 1.10m));
        var breadProduct = (await bread.Content.ReadFromJsonAsync<ProductResponse>())!;
        await SetStockAsync(client, token, storeId, milk.Id, milkStock);
        await SetStockAsync(client, token, storeId, breadProduct.Id, breadStock);
        return new Shop(admin, storeId, token, milk, breadProduct);
    }

    public static async Task SetStockAsync(HttpClient client, string token, Guid storeId, Guid productId, int quantity)
    {
        if (quantity > 0)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/products/{productId}/stock-adjustments", token, new AdjustStockRequest(quantity))).StatusCode);
        }
    }

    public static Task<HttpResponseMessage> PlaceAsync(HttpClient client, string? token, Guid storeId, object body) =>
        SendAsync(client, HttpMethod.Post, $"/stores/{storeId}/orders", token, body);

    public static PlaceOrderRequest Order(string address = "1 High Street", params (Guid ProductId, int Quantity)[] lines) =>
        new(address, [.. lines.Select(line => new OrderLineRequest(line.ProductId, line.Quantity))]);

    public static async Task<int?> StockAsync(Guid productId) =>
        await PostgresServer.ScalarAsync<int?>(await PostgresServer.ApplicationConnectionStringAsync(), $"select quantity_on_hand from inventory_items where product_id = '{productId}'");

    public static async Task<long> OrdersInAsync(Guid storeId) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from orders where store_id = '{storeId}'");

    public static async Task<OrderResponse> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
