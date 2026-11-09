using GrocerEase.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GrocerEase.UnitTests.Infrastructure;

public class DatabaseHealthCheckRegistrationTests
{
    private static HealthCheckRegistration TheRegistration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=never_connected",
                ["Jwt:SecretKey"] = "unit-test-signing-key-0123456789-abcdef",
                ["Jwt:Issuer"] = "issuer-x",
                ["Jwt:Audience"] = "audience-x",
                ["Jwt:ExpiryMinutes"] = "15",
            })
            .Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();

        return Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);
    }

    [Fact]
    public void TheDatabaseCheck_IsRegisteredUnderTheNameDatabase() =>
        Assert.Equal("database", TheRegistration().Name);

    [Fact]
    public void ADatabaseFailure_IsUnhealthy_NotJustDegraded() =>
        Assert.Equal(HealthStatus.Unhealthy, TheRegistration().FailureStatus);

    [Fact]
    public void TheCheck_HasATimeout_SoAHungDatabaseCannotHangTheProbe() =>
        Assert.Equal(TimeSpan.FromSeconds(5), TheRegistration().Timeout);
}
