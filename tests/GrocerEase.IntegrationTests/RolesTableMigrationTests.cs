using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The AddRoles migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the
/// database, not what EF thinks of it.
/// </summary>
public class RolesTableMigrationTests
{
    [Fact]
    public async Task MigratingCreatesTheRolesTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '-') || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'roles'
            """);

        Assert.Equal("id:uuid:-:NO, name:character varying:50:NO", columns);
    }

    [Fact]
    public async Task TheRolesTable_StartsWithCustomerAndAdmin_UnderTheirFixedIds()
    {
        var database = await MigratedDatabaseAsync();

        var rows = await PostgresServer.ScalarAsync<string>(
            database, "select string_agg(id || ':' || name, ', ' order by name) from roles");

        Assert.Equal($"{RoleIds.Admin}:Admin, {RoleIds.Customer}:Customer", rows);
    }

    [Fact]
    public async Task TheIdIsThePrimaryKey_AndTheNameHasAUniqueIndex()
    {
        var database = await MigratedDatabaseAsync();

        var key = await PostgresServer.ScalarAsync<string>(
            database, "select conname || ':' || pg_get_constraintdef(oid) from pg_constraint where conrelid = 'roles'::regclass and contype = 'p'");
        var index = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_roles_name'");

        Assert.Equal("pk_roles:PRIMARY KEY (id)", key);
        Assert.Equal("CREATE UNIQUE INDEX ix_roles_name ON public.roles USING btree (name)", index);
    }

    [Fact]
    public async Task ASecondRoleWithTheSameName_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, "insert into roles (name) values ('Admin')"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_roles_name", exception.ConstraintName);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, "insert into roles (id, name) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', 'Cook')"));

        Assert.Equal("ck_roles_id_is_uuid_v7", exception.ConstraintName);
    }

    [Fact]
    public async Task ARowInsertedWithoutAnId_GetsAVersion7IdFromTheDatabase()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into roles (name) values ('Cook')");

        var version = await PostgresServer.ScalarAsync<int>(database, "select uuid_extract_version(id)::int from roles where name = 'Cook'");

        Assert.Equal(7, version);
    }

    [Fact]
    public async Task ANameLongerThanTheColumn_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, $"insert into roles (name) values ('{new string('a', Role.MaxNameLength + 1)}')"));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, exception.SqlState);
    }
}
