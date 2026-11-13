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

    /// <summary>Where the app under test writes its log files, unless a test chooses its own.</summary>
    public static readonly string TestLogDirectory = Path.Combine(Path.GetTempPath(), "grocerease-integration-test-logs");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The API under test talks to a real PostgreSQL, as the least-privilege app role, on a database the migrator has
        // already migrated (once per test run). Blocking here is fine: it runs once, on a test thread, before the host starts.
        var connectionString = PostgresServer.ApplicationConnectionStringAsync().GetAwaiter().GetResult();

        // Added last, so it wins over user-secrets or environment variables on a developer's machine.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Jwt:SecretKey"] = JwtSecretKey,
                ["ConnectionStrings:Default"] = connectionString,

                // Tests must not write log files into the source tree. One folder for the whole run, outside the repository.
                ["LogFiles:Directory"] = TestLogDirectory,

                // Nothing in the tests may send logs to a Seq that happens to be running on the developer's machine.
                ["Seq:ServerUrl"] = string.Empty,

                // Tests share one client and log in and register many times; the real limits would block them.
                // The rate limit's own tests set small limits on their own app instance.
                ["RateLimiting:Login:PermitLimit"] = "1000",
                ["RateLimiting:Register:PermitLimit"] = "1000",
                ["RateLimiting:Refresh:PermitLimit"] = "1000",
                ["RateLimiting:Health:PermitLimit"] = "1000",
                ["RateLimiting:PlaceOrder:PermitLimit"] = "1000",
            }));
    }
}
