using System.Net;

namespace GrocerEase.IntegrationTests;

public class SmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Get_Test_ReturnsOkWithBody()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test", await response.Content.ReadAsStringAsync());
    }
}
