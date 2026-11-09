using GrocerEase.Api.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace GrocerEase.Api.Health;

public static class HealthEndpointsExtensions
{
    /// <summary>
    /// Maps the two health endpoints. Both are open to anyone (a probe has no token) and answer 200 when healthy, 503 when not.
    /// <list type="bullet">
    /// <item><c>/health</c>: readiness. Runs every check, so it includes the database. Rate limited per client, since each call queries it.</item>
    /// <item><c>/health/live</c>: liveness. Runs no check at all, so it is 200 whenever the process can answer, even with the database down.
    /// That is what a probe that decides to <i>restart</i> the app should use: restarting does not fix a database outage.</item>
    /// </list>
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.HealthPolicy);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync })
            .AllowAnonymous();

        return endpoints;
    }
}
