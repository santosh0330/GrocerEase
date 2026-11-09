using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure.Authentication;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        // The connection string is read when the settings are first needed (not here), and checked when the app starts.
        services.AddOptions<DatabaseSettings>()
            .Configure(settings =>
                settings.ConnectionString = configuration.GetConnectionString(DatabaseSettings.ConnectionStringName) ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped: one context, and so one unit of work, per request. A DbContext is not thread safe and must not be shared.
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.ConfigureNpgsql(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
