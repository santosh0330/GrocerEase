using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The AddRoleClaims migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe
/// the database, not what EF thinks of it.
/// </summary>
public class RoleClaimsTableMigrationTests
{
    private const string Cook = "0192f1a2-3b4c-7d5e-8f6a-1b2c3d4e5f70";

    // Found by name, not by position, so a migration added later does not change what these tests mean.
    private static string Migration(string name) =>
        AllMigrations().Single(migration => migration.EndsWith($"_{name}", StringComparison.Ordinal));

    private static string Before(string name) => AllMigrations()[AllMigrations().ToList().IndexOf(Migration(name)) - 1];

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '-') || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'role_claims'
            """);

        Assert.Equal(
            "id:uuid:-:NO, role_id:uuid:-:NO, claim_type:character varying:50:NO, claim_value:character varying:100:NO", columns);
    }

    [Fact]
    public async Task TheTable_StartsWithWhatEachRoleMayDo()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || ':' || c.claim_type || '=' || c.claim_value, ', ' order by r.name, c.claim_value)
            from role_claims c join roles r on r.id = c.role_id
            """);

        Assert.Equal(
            "Admin:permission=products.read, Admin:permission=profile.read, Admin:permission=roles.read, Admin:permission=stores.manage, Admin:permission=stores.read, "
            + "Customer:permission=products.read, Customer:permission=profile.read, Customer:permission=stores.read, "
            + "StoreUser:permission=inventory.manage, StoreUser:permission=products.manage, StoreUser:permission=products.read, StoreUser:permission=profile.read, StoreUser:permission=stores.read", granted);
    }

    [Fact]
    public async Task TheSeededClaims_HaveVersion7Ids()
    {
        var database = await MigratedDatabaseAsync();

        Assert.Equal(13L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where uuid_extract_version(id) = 7"));
    }

    [Fact]
    public async Task TheIdIsThePrimaryKey_AndTheTripleHasAUniqueIndex()
    {
        var database = await MigratedDatabaseAsync();

        var key = await PostgresServer.ScalarAsync<string>(
            database, "select conname || ':' || pg_get_constraintdef(oid) from pg_constraint where conrelid = 'role_claims'::regclass and contype = 'p'");
        var index = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_role_claims_role_id_claim_type_claim_value'");

        Assert.Equal("pk_role_claims:PRIMARY KEY (id)", key);
        Assert.Equal(
            "CREATE UNIQUE INDEX ix_role_claims_role_id_claim_type_claim_value ON public.role_claims USING btree (role_id, claim_type, claim_value)",
            index);
    }

    [Fact]
    public async Task ARoleCannotMakeTheSameStatementTwice_ButAnotherRoleCan()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database,
            $"insert into role_claims (role_id, claim_type, claim_value) values ('{RoleIds.Admin}', 'permission', 'roles.read')"));
        await PostgresServer.ExecuteAsync(
            database, $"insert into role_claims (role_id, claim_type, claim_value) values ('{RoleIds.Customer}', 'permission', 'roles.read')");

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_role_claims_role_id_claim_type_claim_value", exception.ConstraintName);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database,
            $"insert into role_claims (id, role_id, claim_type, claim_value) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', '{RoleIds.Admin}', 'permission', 'x')"));

        Assert.Equal("ck_role_claims_id_is_uuid_v7", exception.ConstraintName);
    }

    [Fact]
    public async Task ARowInsertedWithoutAnId_GetsAVersion7IdFromTheDatabase()
    {
        var database = await MigratedDatabaseAsync();

        var version = await PostgresServer.ScalarAsync<int>(
            database,
            $"insert into role_claims (role_id, claim_type, claim_value) values ('{RoleIds.Customer}', 'permission', 'x') returning uuid_extract_version(id)::int");

        Assert.Equal(7, version);
    }

    [Fact]
    public async Task AClaimForARoleThatDoesNotExist_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database,
            $"insert into role_claims (role_id, claim_type, claim_value) values ('{Cook}', 'permission', 'x')"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_role_claims_roles_role_id", exception.ConstraintName);
    }

    [Fact]
    public async Task DeletingARole_DeletesItsClaims_ButNotAnotherRolesClaims()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, $"insert into roles (id, name) values ('{Cook}', 'Cook')");
        await PostgresServer.ExecuteAsync(
            database, $"insert into role_claims (role_id, claim_type, claim_value) values ('{Cook}', 'permission', 'roles.read')");

        await PostgresServer.ExecuteAsync(database, $"delete from roles where id = '{Cook}'");

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, $"select count(*) from role_claims where role_id = '{Cook}'"));
        Assert.Equal(13L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims"));
    }

    [Fact]
    public async Task ATypeOrValueLongerThanItsColumn_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var type = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into role_claims (role_id, claim_type, claim_value) values ('{RoleIds.Admin}', '{new string('a', RoleClaim.MaxTypeLength + 1)}', 'x')"));
        var value = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into role_claims (role_id, claim_type, claim_value) values ('{RoleIds.Admin}', 'x', '{new string('b', RoleClaim.MaxValueLength + 1)}')"));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, type.SqlState);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, value.SqlState);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTable_ButKeepsTheRolesAndTheUsers()
    {
        var database = await MigratedDatabaseAsync();

        await MigrateAsync(database, Before("AddRoleClaims"));

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.role_claims')::text"));
        Assert.Equal(2L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from roles"));
    }
}
