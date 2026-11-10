using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure;
using GrocerEase.Infrastructure.Authentication;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrocerEase.UnitTests.Infrastructure;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider(validateScopes: true);
    }

    private static Dictionary<string, string?> ValidSettings() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ConnectionStrings:Default"] = "Host=localhost;Database=never_connected", // nothing here opens a connection
        ["Jwt:SecretKey"] = "unit-test-signing-key-0123456789-abcdef",
        ["Jwt:Issuer"] = "issuer-x",
        ["Jwt:Audience"] = "audience-x",
        ["Jwt:ExpiryMinutes"] = "15",
        ["RefreshToken:LifetimeDays"] = "3",
        ["RefreshToken:MaxLifetimeDays"] = "30",
    };

    [Fact]
    public void EveryPortIsResolvable()
    {
        using var provider = Build(ValidSettings());
        using var scope = provider.CreateScope(); // the repository is scoped, like the context behind it

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefreshTokenGenerator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRefreshTokenPolicy>());
    }

    [Fact]
    public void TheRefreshTokenPolicy_IsBoundFromTheRefreshTokenSection()
    {
        var settings = ValidSettings();
        settings["RefreshToken:LifetimeDays"] = "2";
        settings["RefreshToken:MaxLifetimeDays"] = "20";
        using var provider = Build(settings);

        var policy = provider.GetRequiredService<IRefreshTokenPolicy>();

        Assert.Equal((TimeSpan.FromDays(2), TimeSpan.FromDays(20)), (policy.Lifetime, policy.MaxLifetime));
    }

    [Theory]
    [InlineData("31", "30", "RefreshToken:LifetimeDays")]
    [InlineData("3", "31", "RefreshToken:MaxLifetimeDays")]
    [InlineData("10", "5", "must not be more than")]
    public void ARefreshTokenSettingAboveTheLimit_IsRefusedWhenTheSettingsAreRead(string lifetime, string maxLifetime, string expected)
    {
        var settings = ValidSettings();
        settings["RefreshToken:LifetimeDays"] = lifetime;
        settings["RefreshToken:MaxLifetimeDays"] = maxLifetime;
        using var provider = Build(settings);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IRefreshTokenPolicy>());

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUserRepositoryIsScoped_OneInstanceAndOneContextPerRequest()
    {
        using var provider = Build(ValidSettings());
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var inFirst = first.ServiceProvider.GetRequiredService<IUserRepository>();

        Assert.Same(inFirst, first.ServiceProvider.GetRequiredService<IUserRepository>());
        Assert.NotSame(inFirst, second.ServiceProvider.GetRequiredService<IUserRepository>());
    }

    [Fact]
    public void TheUserRepositoryCannotBeResolvedOutsideARequestScope_SoItCannotBeSharedBetweenRequests()
    {
        using var provider = Build(ValidSettings());

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IUserRepository>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AMissingConnectionString_IsRefusedWhenTheSettingsAreRead(string? connectionString)
    {
        var settings = ValidSettings();
        settings["ConnectionStrings:Default"] = connectionString;
        using var provider = Build(settings);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<DatabaseSettings>>().Value);

        Assert.Contains("ConnectionStrings:Default", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSettingsAreBoundFromTheJwtSection()
    {
        using var provider = Build(ValidSettings());

        var settings = provider.GetRequiredService<IOptions<JwtSettings>>().Value;

        Assert.Equal(("issuer-x", "audience-x", 15), (settings.Issuer, settings.Audience, settings.ExpiryMinutes));
        Assert.Equal("unit-test-signing-key-0123456789-abcdef", settings.SecretKey);
    }

    [Fact]
    public void AWeakKey_IsRefusedWhenTheSettingsAreRead()
    {
        var settings = ValidSettings();
        settings["Jwt:SecretKey"] = "short";
        using var provider = Build(settings);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<JwtSettings>>().Value);

        Assert.Contains("Jwt:SecretKey must be at least 32 characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInfrastructure(new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddInfrastructure(null!));
    }
}
