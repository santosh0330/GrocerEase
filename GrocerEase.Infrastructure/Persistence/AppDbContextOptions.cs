using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>The one place that says how the context talks to PostgreSQL, so the app and the migration tool cannot disagree.</summary>
public static class AppDbContextOptions
{
    /// <summary>The schema holding Entity Framework's own bookkeeping table, apart from the application's tables.</summary>
    public const string MigrationsSchema = "migrations";

    public static DbContextOptionsBuilder<AppDbContext> Configure(this DbContextOptionsBuilder<AppDbContext> builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigureNpgsql(builder, connectionString);
        return builder;
    }

    public static DbContextOptionsBuilder ConfigureNpgsql(this DbContextOptionsBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // The history table goes in its own schema: the API's database role is granted rows in "public" only (see
        // docker/postgres/init/01-roles.sh), so it can neither read nor tamper with the record of which migrations ran.
        return builder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", MigrationsSchema));
    }
}
