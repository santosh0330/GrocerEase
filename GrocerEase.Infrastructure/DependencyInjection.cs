using GrocerEase.Application.Abstractions;
using GrocerEase.Infrastructure.Authentication;
using GrocerEase.Infrastructure.Health;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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

        services.AddOptions<RefreshTokenSettings>()
            .Bind(configuration.GetSection(RefreshTokenSettings.SectionName))
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
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>(); // scoped: it begins a transaction on the same context the repositories use

        // Answers "can this instance reach and use its database?" for the /health endpoint. The timeout stops a hung database
        // from hanging the probe: after five seconds the check is reported unhealthy.
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
            "database", failureStatus: HealthStatus.Unhealthy, timeout: TimeSpan.FromSeconds(5));
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<IRefreshTokenPolicy>(provider => provider.GetRequiredService<IOptions<RefreshTokenSettings>>().Value);

        return services;
    }
}
