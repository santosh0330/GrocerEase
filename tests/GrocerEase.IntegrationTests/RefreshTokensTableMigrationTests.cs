using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The AddRefreshTokens migration on a database that already has the users table, checked against the real schema.
/// Raw SQL on purpose: these tests describe the database, not what EF thinks of it.
/// </summary>
public class RefreshTokensTableMigrationTests
{
    private const string UserId = "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f60";
    private const string TokenId = "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f61";
    private const string FamilyId = "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f62";
    private const string Hash = "decode(repeat('ab', 32), 'hex')";

    private static async Task<string> MigratedAsync()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(
            database,
            $"insert into users (id, first_name, last_name, email, password_hash, role_id) values ('{UserId}', 'Ada', 'Lovelace', 'ada@example.com', 'hash', '{RoleIds.Customer}')");
        return database;
    }

    private static string Insert(
        string id = TokenId, string hash = Hash, string created = "now()", string expires = "now() + interval '3 days'",
        string family = "now() + interval '30 days'", string user = UserId) =>
        $"insert into refresh_tokens (id, user_id, family_id, token_hash, created_at, expires_at, family_expires_at) values ('{id}', '{user}', '{FamilyId}', {hash}, {created}, {expires}, {family})";

    private static async Task<PostgresException> RefusedAsync(string database, string sql) =>
        await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, sql));

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns_AndNoColumnThatCouldHoldTheToken()
    {
        var database = await MigratedAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'refresh_tokens'
            """);

        Assert.Equal(
            "id:uuid:NO, user_id:uuid:NO, family_id:uuid:NO, token_hash:bytea:NO, created_at:timestamp with time zone:NO, "
            + "expires_at:timestamp with time zone:NO, family_expires_at:timestamp with time zone:NO, "
            + "consumed_at:timestamp with time zone:YES, revoked_at:timestamp with time zone:YES",
            columns);
    }

    [Fact]
    public async Task AValidRowIsAccepted_AndStartsUnconsumedAndUnrevoked()
    {
        var database = await MigratedAsync();

        await PostgresServer.ExecuteAsync(database, Insert());

        Assert.Equal(
            "null,null",
            await PostgresServer.ScalarAsync<string>(database, "select coalesce(consumed_at::text, 'null') || ',' || coalesce(revoked_at::text, 'null') from refresh_tokens"));
    }

    [Fact]
    public async Task TheHashHasAUniqueIndex()
    {
        var database = await MigratedAsync();
        await PostgresServer.ExecuteAsync(database, Insert());

        var exception = await RefusedAsync(database, Insert(id: "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f63"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_refresh_tokens_token_hash", exception.ConstraintName);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused()
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(id: "7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1"));

        Assert.Equal("ck_refresh_tokens_id_is_uuid_v7", exception.ConstraintName);
    }

    [Theory]
    [InlineData("decode('abab', 'hex')")]
    [InlineData("decode(repeat('ab', 33), 'hex')")]
    public async Task AHashThatIsNotThirtyTwoBytes_IsRefused(string hash)
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(hash: hash));

        Assert.Equal("ck_refresh_tokens_token_hash_is_sha256", exception.ConstraintName);
    }

    [Fact]
    public async Task ATokenThatExpiresBeforeItIsCreated_IsRefused()
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(expires: "now() - interval '1 second'"));

        Assert.Equal("ck_refresh_tokens_expires_after_created", exception.ConstraintName);
    }

    [Fact]
    public async Task ATokenThatOutlivesItsFamily_IsRefused()
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(expires: "now() + interval '3 days'", family: "now() + interval '2 days'"));

        Assert.Equal("ck_refresh_tokens_within_family", exception.ConstraintName);
    }

    [Fact]
    public async Task ATokenThatLivesMoreThanThirtyDays_IsRefused_EvenIfItsFamilyAllowsIt()
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(expires: "now() + interval '31 days'", family: "now() + interval '40 days'"));

        Assert.Equal("ck_refresh_tokens_at_most_30_days", exception.ConstraintName);
    }

    [Fact]
    public async Task ATokenThatLivesExactlyThirtyDays_IsAccepted()
    {
        var database = await MigratedAsync();

        await PostgresServer.ExecuteAsync(database, Insert(created: "timestamptz '2026-01-01 00:00:00+00'", expires: "timestamptz '2026-01-31 00:00:00+00'", family: "timestamptz '2026-01-31 00:00:00+00'"));
    }

    [Fact]
    public async Task ATokenForAUserThatDoesNotExist_IsRefused()
    {
        var database = await MigratedAsync();

        var exception = await RefusedAsync(database, Insert(user: "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f99"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Fact]
    public async Task DeletingTheUser_DeletesTheirTokens()
    {
        var database = await MigratedAsync();
        await PostgresServer.ExecuteAsync(database, Insert());

        await PostgresServer.ExecuteAsync(database, $"delete from users where id = '{UserId}'");

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from refresh_tokens"));
    }

    [Fact]
    public async Task TheFamilyAndTheUserAreIndexed()
    {
        var database = await MigratedAsync();

        var indexes = await PostgresServer.ScalarAsync<string>(
            database, "select string_agg(indexname, ',' order by indexname) from pg_indexes where tablename = 'refresh_tokens'");

        Assert.Equal(
            "ix_refresh_tokens_family_id,ix_refresh_tokens_token_hash,ix_refresh_tokens_user_id,pk_refresh_tokens", indexes);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTableAndKeepsTheUsers()
    {
        var database = await MigratedAsync();
        var migrations = AllMigrations();
        var thisMigration = migrations.ToList().FindIndex(name => name.EndsWith("_AddRefreshTokens", StringComparison.Ordinal));

        // Found by name, not by position, so a migration added after this one does not change what this test rolls back.
        await MigrateAsync(database, migrations[thisMigration - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.refresh_tokens')::text"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users"));
    }

    [Fact]
    public void ThisMigrationExists_AndFollowsTheOnesThatCreateTheUsersTable() =>
        Assert.True(
            AllMigrations().ToList().FindIndex(name => name.EndsWith("_AddRefreshTokens", StringComparison.Ordinal)) >= 2,
            "AddRefreshTokens must come after CreateUsers and AddUserTimestamps.");
}
