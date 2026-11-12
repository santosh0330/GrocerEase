using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The AddUserRole migration: every user holds exactly one role. Checked against the real schema, with raw SQL on purpose:
/// these tests describe the database, not what EF thinks of it. The delicate part is the backfill, so it has its own tests.
/// </summary>
public class UserRoleMigrationTests
{
    private static string UniqueEmail() => $"ada-{Guid.NewGuid():N}@example.com";

    private static string InsertUser(string email, Guid? roleId) =>
        roleId is null
            ? $"insert into users (first_name, last_name, email, password_hash) values ('Ada', 'Lovelace', '{email}', 'hash')"
            : $"insert into users (first_name, last_name, email, password_hash, role_id) values ('Ada', 'Lovelace', '{email}', 'hash', '{roleId}')";

    // Found by name, not by position, so a migration added later does not change what these tests mean.
    private static string Migration(string name) =>
        AllMigrations().Single(migration => migration.EndsWith($"_{name}", StringComparison.Ordinal));

    private static string Before(string name) => AllMigrations()[AllMigrations().ToList().IndexOf(Migration(name)) - 1];

    [Fact]
    public async Task UsersThatExistedBeforeTheMigration_EndUpAsCustomers_AndTheirUpdatedAtIsNotTouched()
    {
        var database = await PostgresServer.CreateEmptyDatabaseAsync();
        await MigrateAsync(database, Before("AddUserRole"));
        await PostgresServer.ExecuteAsync(database, InsertUser("ada@example.com", roleId: null));
        await PostgresServer.ExecuteAsync(database, InsertUser("grace@example.com", roleId: null));
        await PostgresServer.ExecuteAsync(database, "select pg_sleep(0.05)"); // a later transaction, so a stray update would show

        await MigrateAsync(database);

        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, $"select count(*) from users where role_id = '{RoleIds.Customer}'"));
        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users where updated_at = created_at"));
    }

    [Fact]
    public async Task TheColumn_IsRequired_AndHasNoDefault_SoAForgottenRoleIsRefusedNotGuessed()
    {
        var database = await MigratedDatabaseAsync();

        var column = await PostgresServer.ScalarAsync<string>(
            database,
            "select data_type || ':' || is_nullable || ':' || coalesce(column_default, '-') from information_schema.columns where table_name = 'users' and column_name = 'role_id'");
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), roleId: null)));

        Assert.Equal("uuid:NO:-", column);
        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
    }

    [Fact]
    public async Task AUserCanHoldAnySeededRole()
    {
        var database = await MigratedDatabaseAsync();

        await PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), RoleIds.Customer));
        await PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), RoleIds.Admin));

        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, "select count(distinct role_id) from users"));
    }

    [Fact]
    public async Task AUserWhoseRoleDoesNotExist_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), Guid.CreateVersion7())));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_users_roles_role_id", exception.ConstraintName);
    }

    [Fact]
    public async Task ARoleThatUsersHold_CannotBeDeleted()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), RoleIds.Admin));

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, $"delete from roles where id = '{RoleIds.Admin}'"));

        // ON DELETE RESTRICT has its own state, 23001. A plain foreign key violation (23503) is what an insert of an unknown role gets.
        Assert.Equal(PostgresErrorCodes.RestrictViolation, exception.SqlState);
        Assert.Equal("fk_users_roles_role_id", exception.ConstraintName);
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users"));
    }

    [Fact]
    public async Task ARoleNoOneHolds_CanBeDeleted()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), RoleIds.Customer));

        await PostgresServer.ExecuteAsync(database, $"delete from roles where id = '{RoleIds.Admin}'");

        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from roles")); // three seeded, one deleted
    }

    [Fact]
    public async Task TheRoleColumn_HasAnIndex()
    {
        var database = await MigratedDatabaseAsync();

        var definition = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_users_role_id'");

        Assert.Equal("CREATE INDEX ix_users_role_id ON public.users USING btree (role_id)", definition);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheColumn_ButKeepsTheUsersAndTheRoles()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, InsertUser(UniqueEmail(), RoleIds.Admin));

        await MigrateAsync(database, Before("AddUserRole"));

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(
            database, "select count(*) from information_schema.columns where table_name = 'users' and column_name = 'role_id'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users"));
        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from roles"));
    }
}
