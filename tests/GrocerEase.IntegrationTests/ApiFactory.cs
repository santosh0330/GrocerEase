using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Hosts the real API in memory. Override <c>ConfigureWebHost</c> here to replace
/// infrastructure (for example the database) once it exists.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>A key for tests only. It is not a secret: it signs tokens nobody trusts outside the test run.</summary>
    public const string JwtSecretKey = "integration-tests-only-signing-key-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Added last, so it wins over user-secrets or environment variables on a developer's machine.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jwt:SecretKey"] = JwtSecretKey,

                // Tests share one client and log in and register many times; the real limits would block them.
                // The rate limit's own tests set small limits on their own app instance.
                ["RateLimiting:Login:PermitLimit"] = "1000",
                ["RateLimiting:Register:PermitLimit"] = "1000",
            }));
    }
}
