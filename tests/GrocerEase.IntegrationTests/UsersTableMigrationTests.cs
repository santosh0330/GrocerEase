using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The CreateUsers migration, run by the migrator role on an empty database, checked against the real schema it leaves.
/// Raw SQL on purpose: these tests describe the database, not what EF thinks of it.
/// </summary>
public class UsersTableMigrationTests
{
    // These tests describe what CreateUsers leaves behind, so they stop after it: later migrations have their own tests.
    private static async Task<string> AfterCreateUsersAsync()
    {
        var connectionString = await PostgresServer.CreateEmptyDatabaseAsync();
        await MigrateAsync(connectionString, AllMigrations()[0]);
        return connectionString;
    }

    [Fact]
    public async Task MigratingAnEmptyDatabase_CreatesTheUsersTableWithTheExpectedColumns()
    {
        var database = await AfterCreateUsersAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '-') || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'users'
            """);

        Assert.Equal(
            "id:uuid:-:NO, first_name:character varying:100:NO, last_name:character varying:100:NO, email:character varying:254:NO, password_hash:character varying:256:NO",
            columns);
    }

    [Fact]
    public async Task TheIdIsThePrimaryKey()
    {
        var database = await AfterCreateUsersAsync();

        var key = await PostgresServer.ScalarAsync<string>(
            database,
            "select conname || ':' || pg_get_constraintdef(oid) from pg_constraint where conrelid = 'users'::regclass and contype = 'p'");

        Assert.Equal("pk_users:PRIMARY KEY (id)", key);
    }

    [Fact]
    public async Task TheEmailHasAUniqueIndex()
    {
        var database = await AfterCreateUsersAsync();

        var definition = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_users_email'");

        Assert.Equal("CREATE UNIQUE INDEX ix_users_email ON public.users USING btree (email)", definition);
    }

    [Fact]
    public async Task TheSameEmailTwice_IsRefusedByTheUniqueIndex()
    {
        var database = await AfterCreateUsersAsync();
        const string insert = "insert into users (first_name, last_name, email, password_hash) values ('Ada', 'Lovelace', 'ada@example.com', 'hash')";
        await PostgresServer.ExecuteAsync(database, insert);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, insert));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_users_email", exception.ConstraintName);
    }

    [Fact]
    public async Task ARowInsertedWithoutAnId_GetsAVersion7UuidFromTheDatabase()
    {
        var database = await AfterCreateUsersAsync();

        var version = await PostgresServer.ScalarAsync<short>(
            database,
            "insert into users (first_name, last_name, email, password_hash) values ('Ada', 'Lovelace', 'ada@example.com', 'hash') returning uuid_extract_version(id)");

        Assert.Equal(7, version);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefusedByTheDatabase()
    {
        var database = await AfterCreateUsersAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database,
            "insert into users (id, first_name, last_name, email, password_hash) values (gen_random_uuid(), 'Ada', 'Lovelace', 'ada@example.com', 'hash')"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_users_id_is_uuid_v7", exception.ConstraintName);
    }

    [Fact]
    public async Task TheMigrationHistoryIsKeptInItsOwnSchema_NotNextToTheTables()
    {
        var database = await AfterCreateUsersAsync();

        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from migrations.\"__EFMigrationsHistory\""));
        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.\"__EFMigrationsHistory\"')::text"));
    }

    [Fact]
    public async Task MigratingTwice_ChangesNothingTheSecondTime()
    {
        var database = await AfterCreateUsersAsync();

        await MigrateAsync(database, AllMigrations()[0]);

        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from migrations.\"__EFMigrationsHistory\""));
    }

    [Fact]
    public async Task RollingBackToTheStart_RemovesTheUsersTable()
    {
        var database = await AfterCreateUsersAsync();

        await MigrateAsync(database, targetMigration: "0");

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.users')::text"));
    }
}
