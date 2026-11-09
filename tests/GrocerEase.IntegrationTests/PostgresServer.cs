using Npgsql;
using Testcontainers.PostgreSql;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// One real PostgreSQL 18 for the whole test run, started the first time a test needs it and removed when the run ends
/// (by Testcontainers' cleanup container, so a crashed run does not leave it behind). It is configured like the
/// <c>db</c> service in docker-compose.yml: it runs as the non-root <c>postgres</c> user and is set up by the real init
/// script, so the roles the tests see are the roles the application gets.
/// </summary>
internal static class PostgresServer
{
    public const string Database = "grocerease";

    // These must match docker/postgres/init/01-roles.sh. They are passwords for a throwaway container, not secrets.
    public const string AdminUser = "grocerease_admin";
    public const string AdminPassword = "admin-password-for-tests";
    public const string MigratorUser = "grocerease_migrator";
    public const string MigratorPassword = "migrator-password-for-tests";
    public const string AppUser = "grocerease_app";
    public const string AppPassword = "app-password-for-tests";

    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var script = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "postgres-init", "01-roles.sh"));

        var container = new PostgreSqlBuilder("postgres:18")
            .WithDatabase(Database)
            .WithUsername(AdminUser)
            .WithPassword(AdminPassword)
            .WithEnvironment("MIGRATOR_PASSWORD", MigratorPassword)
            .WithEnvironment("APP_PASSWORD", AppPassword)
            .WithResourceMapping(
                script,
                "/docker-entrypoint-initdb.d/01-roles.sh",
                Convert.ToUInt32("755", 8)) // rwxr-xr-x: the postgres user runs the script but does not own it
            .WithCreateParameterModifier(parameters => parameters.User = "postgres") // like "user: postgres" in docker-compose.yml
            .Build();
        await container.StartAsync();
        return container;
    }

    public static async Task<PostgreSqlContainer> ContainerAsync() => await Container.Value;

    /// <summary>A connection string for one of the roles, to the application database unless another is named.</summary>
    public static async Task<string> ConnectionStringAsync(string user, string password, string? database = null)
    {
        var container = await Container.Value;
        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Username = user,
            Password = password,
            Database = database ?? Database,
            Pooling = false, // each test opens its own short-lived connections and must not see another's session
        }.ConnectionString;
    }

    public static Task<string> AppConnectionStringAsync() => ConnectionStringAsync(AppUser, AppPassword);

    public static Task<string> MigratorConnectionStringAsync() => ConnectionStringAsync(MigratorUser, MigratorPassword);

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }
}
