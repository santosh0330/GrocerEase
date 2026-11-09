using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The migration that adds created_at and updated_at. The database keeps both: the application never sends them, and an
/// UPDATE from any source (the API, a script, a person with psql) moves updated_at, because a trigger does it.
/// </summary>
public class UserTimestampsMigrationTests
{
    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static string Insert(string email) =>
        $"insert into users (first_name, last_name, email, password_hash) values ('Ada', 'Lovelace', '{email}', 'hash')";

    [Fact]
    public async Task TheTimestampColumns_AreRequired_HaveATimeZone_AndDefaultToNow()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(column_default, '-'), ', ' order by column_name)
            from information_schema.columns where table_schema = 'public' and table_name = 'users' and column_name in ('created_at', 'updated_at')
            """);

        Assert.Equal(
            "created_at:timestamp with time zone:NO:now(), updated_at:timestamp with time zone:NO:now()",
            columns);
    }

    [Fact]
    public async Task ANewRow_GetsBothTimestampsFromTheDatabase_AtTheSameMoment()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, Insert("ada@example.com"));

        var close = await PostgresServer.ScalarAsync<bool>(
            database,
            "select created_at = updated_at and created_at between now() - interval '1 minute' and now() from users");

        Assert.True(close);
    }

    [Fact]
    public async Task AnUpdate_MovesUpdatedAt_AndLeavesCreatedAtAlone()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, Insert("ada@example.com"));
        await PostgresServer.ExecuteAsync(database, "select pg_sleep(0.05)"); // now() is fixed per transaction: use a later one

        await PostgresServer.ExecuteAsync(database, "update users set first_name = 'Augusta'");

        var moved = await PostgresServer.ScalarAsync<bool>(database, "select updated_at > created_at from users");
        Assert.True(moved);
    }

    [Fact]
    public async Task TheTrigger_ExistsOnUsers_AndRunsBeforeEachRowUpdate()
    {
        var database = await MigratedDatabaseAsync();

        var trigger = await PostgresServer.ScalarAsync<string>(
            database,
            "select tgname || ':' || pg_get_triggerdef(oid, false) from pg_trigger where tgrelid = 'users'::regclass and not tgisinternal");

        Assert.Equal(
            "trg_users_set_updated_at:CREATE TRIGGER trg_users_set_updated_at BEFORE UPDATE ON public.users FOR EACH ROW EXECUTE FUNCTION set_updated_at()",
            trigger);
    }

    [Fact]
    public async Task RowsThatExistedBeforeTheMigration_GetTimestamps_NotNulls()
    {
        var database = await PostgresServer.CreateEmptyDatabaseAsync();
        await MigrateAsync(database, AllMigrations()[0]);
        await PostgresServer.ExecuteAsync(database, Insert("ada@example.com"));

        await MigrateAsync(database); // now add the timestamp columns to a table that already holds a user

        var filled = await PostgresServer.ScalarAsync<bool>(database, "select created_at is not null and updated_at is not null from users");
        Assert.True(filled);
    }

    [Fact]
    public async Task RollingBack_RemovesTheColumnsTheTriggerAndTheFunction_ButKeepsTheUsers()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, Insert("ada@example.com"));

        await MigrateAsync(database, AllMigrations()[0]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(
            database, "select count(*) from information_schema.columns where table_name = 'users' and column_name in ('created_at', 'updated_at')"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from pg_trigger where tgrelid = 'users'::regclass and not tgisinternal"));
        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regproc('set_updated_at')::text"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users"));
    }

    [Fact]
    public async Task TheApplicationRole_AlsoMovesUpdatedAtOnAnUpdate_BecauseTheTriggerRunsForWhoeverUpdates()
    {
        var app = await PostgresServer.ApplicationConnectionStringAsync();
        var email = UniqueEmail();
        await PostgresServer.ExecuteAsync(app, Insert(email));
        await PostgresServer.ExecuteAsync(app, "select pg_sleep(0.05)");

        await PostgresServer.ExecuteAsync(app, $"update users set first_name = 'Augusta' where email = '{email}'");

        Assert.True(await PostgresServer.ScalarAsync<bool>(app, $"select updated_at > created_at from users where email = '{email}'"));
    }

    [Fact]
    public async Task EfReadsTheTimestampsBackAfterAnInsert_WithoutTheUserEntityKnowingThem()
    {
        var user = new User(Guid.CreateVersion7(), "Ada", "Lovelace", UniqueEmail(), "hash");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

        Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));

        var created = db.Entry(user).Property<DateTimeOffset>("CreatedAt").CurrentValue;
        Assert.InRange(created, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(created, db.Entry(user).Property<DateTimeOffset>("UpdatedAt").CurrentValue);
    }
}
