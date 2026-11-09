using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GrocerEase.IntegrationTests;

/// <summary>Helpers for tests that run the migrations on a blank database, as the migrator role does.</summary>
internal static class MigrationTestSupport
{
    private static AppDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(connectionString).Options);

    /// <summary>Every migration in the project, oldest first, by id (for example <c>20261109194505_CreateUsers</c>).</summary>
    public static IReadOnlyList<string> AllMigrations()
    {
        using var db = NewContext("Host=localhost;Database=never_connected");
        return [.. db.Database.GetMigrations()];
    }

    /// <summary>A new empty database with every migration applied.</summary>
    public static async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = await PostgresServer.CreateEmptyDatabaseAsync();
        await MigrateAsync(connectionString);
        return connectionString;
    }

    /// <summary>Migrates up (or down) to the named migration, or to the newest when none is named. <c>"0"</c> means no migrations at all.</summary>
    public static async Task MigrateAsync(string connectionString, string? targetMigration = null)
    {
        await using var db = NewContext(connectionString);
        if (targetMigration is null)
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.GetService<IMigrator>().MigrateAsync(targetMigration);
        }
    }
}
