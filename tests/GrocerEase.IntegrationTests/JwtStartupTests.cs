using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GrocerEase.IntegrationTests;

/// <summary>The app must refuse to start with a missing or weak signing key, not fail on the first login.</summary>
public class JwtStartupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private WebApplicationFactory<Program> WithKey(string key) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Jwt:SecretKey"] = key })));

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void TheAppDoesNotStart_WithAMissingOrWeakKey(string key)
    {
        using var app = WithKey(key);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("Jwt:SecretKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppStarts_WithAValidKey()
    {
        using var app = WithKey(ApiFactory.JwtSecretKey);

        using var client = app.CreateClient();

        Assert.NotNull(client);
    }
}
