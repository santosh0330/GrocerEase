using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddStores migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class StoresTableMigrationTests
{
    [Fact]
    public async Task MigratingCreatesTheStoresTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '-') || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'stores'
            """);

        Assert.Equal("id:uuid:-:NO, name:character varying:100:NO, address:character varying:300:NO", columns);
    }

    [Fact]
    public async Task TheIdIsThePrimaryKey_AndTheNameHasAUniqueIndex()
    {
        var database = await MigratedDatabaseAsync();

        var key = await PostgresServer.ScalarAsync<string>(
            database, "select conname || ':' || pg_get_constraintdef(oid) from pg_constraint where conrelid = 'stores'::regclass and contype = 'p'");
        var index = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_stores_name'");

        Assert.Equal("pk_stores:PRIMARY KEY (id)", key);
        Assert.Equal("CREATE UNIQUE INDEX ix_stores_name ON public.stores USING btree (name)", index);
    }

    [Fact]
    public async Task ASecondStoreWithTheSameName_IsRefused_ByTheNamedIndex()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', '1 High Street')");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', '2 Other Road')"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_stores_name", exception.ConstraintName);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, "insert into stores (id, name, address) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', 'Fresh Mart', 'x')"));

        Assert.Equal("ck_stores_id_is_uuid_v7", exception.ConstraintName);
    }

    [Fact]
    public async Task ARowInsertedWithoutAnId_GetsAVersion7IdFromTheDatabase()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");

        Assert.Equal(7, await PostgresServer.ScalarAsync<int>(database, "select uuid_extract_version(id)::int from stores"));
    }

    [Fact]
    public async Task ANameOrAnAddressLongerThanItsColumn_IsRefused()
    {
        var database = await MigratedDatabaseAsync();

        var name = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into stores (name, address) values ('{new string('a', Store.MaxNameLength + 1)}', 'x')"));
        var address = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into stores (name, address) values ('x', '{new string('a', Store.MaxAddressLength + 1)}')"));

        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, name.SqlState);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, address.SqlState);
    }

    [Fact]
    public async Task TheStorePermissions_AreSeededForTheRolesThatHoldThem()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name, c.claim_value)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value like 'stores.%'
            """);

        Assert.Equal("Admin=stores.manage, Admin=stores.read, Customer=stores.read, StoreUser=stores.read", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTableAndItsPermissions_ButKeepsTheRoles()
    {
        var database = await MigratedDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddStores", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.stores')::text"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value like 'stores.%'"));
        Assert.Equal(3L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from roles"));
    }
}
