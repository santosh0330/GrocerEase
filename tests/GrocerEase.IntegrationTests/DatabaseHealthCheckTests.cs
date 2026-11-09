using GrocerEase.Infrastructure.Health;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// What "the database is healthy" means: reachable, usable by the application's role, and migrated. Each way it can fail is tried
/// against a real PostgreSQL, because a fake cannot say what a real connection failure looks like.
/// </summary>
public class DatabaseHealthCheckTests
{
    private static AppDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(connectionString).Options);

    private static Task<HealthCheckResult> CheckAsync(AppDbContext db, CancellationToken cancellationToken = default) =>
        new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext(), cancellationToken);

    [Fact]
    public async Task AReachableMigratedDatabase_UsedByTheAppRole_IsHealthy()
    {
        await using var db = Context(await PostgresServer.ApplicationConnectionStringAsync());

        var result = await CheckAsync(db);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ADatabaseWhoseMigrationsHaveNotRun_IsUnhealthy_BecauseTheUsersTableIsMissing()
    {
        // A blank database the migrator can connect to: reachable, but nothing has been created in it.
        await using var db = Context(await PostgresServer.CreateEmptyDatabaseAsync());

        var result = await CheckAsync(db);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        var exception = Assert.IsType<PostgresException>(result.Exception);
        Assert.Equal(PostgresErrorCodes.UndefinedTable, exception.SqlState);
    }

    [Fact]
    public async Task ADatabaseThatCannotBeReached_IsUnhealthy()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await PostgresServer.ApplicationConnectionStringAsync()) { Port = 1, Timeout = 2 }.ConnectionString;
        await using var db = Context(connectionString);

        var result = await CheckAsync(db);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task AWrongPassword_IsUnhealthy()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(await PostgresServer.ApplicationConnectionStringAsync()) { Password = "not-the-password" }.ConnectionString;
        await using var db = Context(connectionString);

        var result = await CheckAsync(db);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task TheDescription_NeverRepeatsTheConnectionFailure_SoNothingInternalReachesAResponse()
    {
        var builder = new NpgsqlConnectionStringBuilder(await PostgresServer.ApplicationConnectionStringAsync()) { Password = "not-the-password" };
        await using var db = Context(builder.ConnectionString);

        var result = await CheckAsync(db);

        // The exception travels in result.Exception, for the log. The description is what a caller could be shown.
        Assert.Equal("The database check failed.", result.Description);
        Assert.DoesNotContain(builder.Host!, result.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(builder.Username!, result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledCheck_IsNotReportedAsADatabaseFailure_ItPropagates()
    {
        await using var db = Context(await PostgresServer.ApplicationConnectionStringAsync());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CheckAsync(db, cancelled.Token));
    }

    [Fact]
    public void RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new DatabaseHealthCheck(null!));
}
