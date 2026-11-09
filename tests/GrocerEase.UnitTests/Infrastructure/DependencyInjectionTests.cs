using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure;
using GrocerEase.Infrastructure.Authentication;
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
        ["Jwt:SecretKey"] = "unit-test-signing-key-0123456789-abcdef",
        ["Jwt:Issuer"] = "issuer-x",
        ["Jwt:Audience"] = "audience-x",
        ["Jwt:ExpiryMinutes"] = "15",
    };

    [Fact]
    public void EveryPortIsResolvable()
    {
        using var provider = Build(ValidSettings());

        Assert.NotNull(provider.GetRequiredService<IUserRepository>());
        Assert.NotNull(provider.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(provider.GetRequiredService<IJwtTokenGenerator>());
    }

    [Fact]
    public void TheUserRepositoryIsASingleton_SoUsersSurviveFromOneRequestToTheNext()
    {
        using var provider = Build(ValidSettings());

        Assert.Same(provider.GetRequiredService<IUserRepository>(), provider.GetRequiredService<IUserRepository>());
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
