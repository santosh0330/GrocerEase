using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GrocerEase.Infrastructure.Health;

/// <summary>
/// "Can this instance serve requests that need the database?" It reads from the <c>users</c> table, so it only passes when the
/// database is reachable, the application's role may use it, <b>and</b> the migrations have run (the table exists). Merely
/// opening a connection would pass against an empty, unmigrated database and report a service that cannot register anyone as healthy.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // SELECT EXISTS (SELECT 1 FROM users): one cheap query that touches the table without reading any user.
            _ = await _db.Users.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception goes in the result so the health check service writes it to the log, where an operator can read
            // why. The description is fixed text: the endpoint is open to anyone, and the real message names hosts and users.
            return HealthCheckResult.Unhealthy("The database check failed.", exception);
        }
    }
}
