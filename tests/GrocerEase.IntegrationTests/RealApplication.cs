using GrocerEase.Application;
using GrocerEase.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The real handlers, repositories and unit of work wired up as the API wires them, against the real PostgreSQL and as the least-privilege
/// application role, but without HTTP: for tests that need to see what a failure in the middle of a use case leaves behind.
/// </summary>
public static class RealApplication
{
    public static async Task<ServiceProvider> BuildAsync(Action<IServiceCollection>? replace = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:Default"] = await PostgresServer.ApplicationConnectionStringAsync(),
                ["Jwt:SecretKey"] = ApiFactory.JwtSecretKey,
                ["Jwt:Issuer"] = "GrocerEase",
                ["Jwt:Audience"] = "GrocerEase",
                ["Jwt:ExpiryMinutes"] = "10",
                ["RefreshToken:LifetimeDays"] = "3",
                ["RefreshToken:MaxLifetimeDays"] = "30",
            })
            .Build();
        var services = new ServiceCollection().AddLogging().AddApplication().AddInfrastructure(configuration);
        replace?.Invoke(services); // a test may swap one service, for example to pause a repository at a chosen moment
        return services.BuildServiceProvider(validateScopes: true);
    }
}
