using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure.Authentication;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything Infrastructure provides for Application's ports. Api calls this once, so it is the only
    /// place that knows which implementation backs which interface.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        // TEMPORARY: the in-memory repository must be a singleton, or every request would see an empty store.
        services.AddSingleton<IUserRepository, InMemoryUserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
