using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Contracts.Orders;
using Microsoft.Extensions.Options;
using static GrocerEase.IntegrationTests.RateLimitTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Limits how fast one client may place orders: 3 per window in these tests (20 a minute in production). Placing an order takes stock and
/// writes several rows, so it is the first store endpoint to be limited. The orders themselves go to a store that does not exist: a
/// refused request counts like any other, and that keeps these tests about the limit and not about stock.
/// </summary>
public class PlaceOrderRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int Limit = 3;

    private static readonly PlaceOrderRequest AnOrder = new("1 High Street", [new OrderLineRequest(Guid.CreateVersion7(), 1)]);

    private static async Task<HttpResponseMessage> PlaceAsync(HttpClient client, string ip, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/stores/{Guid.CreateVersion7()}/orders", UriKind.Relative))
        {
            Content = JsonContent.Create(AnOrder),
        };
        request.Headers.Add(ClientIpHeader, ip);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> CustomerTokenAsync(HttpClient client, string ip)
    {
        var response = await RegisterAsync(client, ip);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GrocerEase.Contracts.Authentication.AuthenticationResponse>())!.Token;
    }

    [Fact]
    public async Task TheOrdersUpToTheLimitAreAnswered_ThenTheNextOneIs429()
    {
        using var app = factory.WithRateLimits(placeOrderLimit: "3");
        using var client = app.CreateClient();
        var token = await CustomerTokenAsync(client, "198.51.100.30");

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PlaceAsync(client, "198.51.100.30", token)).StatusCode); // answered: the store is not there
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PlaceAsync(client, "198.51.100.30", token)).StatusCode);
    }

    [Fact]
    public async Task TheRejection_IsAProblemJsonWithARetryAfterHeaderOfTheOrderWindow()
    {
        using var app = factory.WithRateLimits(placeOrderLimit: "3", placeOrderWindow: "90", loginWindow: "60");
        using var client = app.CreateClient();
        var token = await CustomerTokenAsync(client, "198.51.100.31");
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await PlaceAsync(client, "198.51.100.31", token);
        }

        var blocked = await PlaceAsync(client, "198.51.100.31", token);

        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("RateLimit.TooManyRequests", body.RootElement.GetProperty("code").GetString());
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out var values));
        Assert.Equal(90, int.Parse(Assert.Single(values), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AnotherClientAddress_IsNotAffected()
    {
        using var app = factory.WithRateLimits(placeOrderLimit: "3");
        using var client = app.CreateClient();
        var token = await CustomerTokenAsync(client, "198.51.100.32");
        for (var attempt = 0; attempt < Limit; attempt++)
        {
            await PlaceAsync(client, "198.51.100.32", token);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PlaceAsync(client, "198.51.100.32", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PlaceAsync(client, "198.51.100.33", token)).StatusCode);
    }

    [Fact]
    public async Task EveryRequestCounts_EvenOneWithNoToken_BecauseTheLimiterRunsBeforeSignIn()
    {
        using var app = factory.WithRateLimits(placeOrderLimit: "3");
        using var client = app.CreateClient();

        for (var attempt = 0; attempt < Limit; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await PlaceAsync(client, "198.51.100.34", token: null)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await PlaceAsync(client, "198.51.100.34", token: null)).StatusCode);
    }

    [Fact]
    public async Task ReadingOrdersIsNotLimited_OnlyPlacingThem()
    {
        using var app = factory.WithRateLimits(placeOrderLimit: "1");
        using var client = app.CreateClient();
        var token = await CustomerTokenAsync(client, "198.51.100.35");
        await PlaceAsync(client, "198.51.100.35", token);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PlaceAsync(client, "198.51.100.35", token)).StatusCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/orders", UriKind.Relative));
            request.Headers.Add(ClientIpHeader, "198.51.100.35");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
    }

    [Theory]
    [InlineData("0", "60")]
    [InlineData("100000", "60")]
    [InlineData("3", "0")]
    [InlineData("3", "3601")]
    public void TheAppDoesNotStart_WithAnInvalidPlaceOrderLimit(string permitLimit, string windowSeconds)
    {
        using var app = factory.WithRateLimits(placeOrderLimit: permitLimit, placeOrderWindow: windowSeconds);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("RateLimiting:PlaceOrder:", exception.Message, StringComparison.Ordinal);
    }
}
