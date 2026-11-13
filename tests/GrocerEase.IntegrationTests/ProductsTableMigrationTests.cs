using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddProducts migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class ProductsTableMigrationTests
{
    private static async Task<(string Database, Guid StoreId)> WithAStoreAsync()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        return (database, await PostgresServer.ScalarAsync<Guid>(database, "select id from stores"));
    }

    private static string Insert(Guid storeId, string name, string price = "1.50", string? archivedAt = null) =>
        $"insert into products (store_id, name, description, price, archived_at) values ('{storeId}', '{name}', '', {price}, {(archivedAt is null ? "null" : $"'{archivedAt}'")})";

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, coalesce(numeric_precision::text || ',' || numeric_scale::text, '-')) || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'products'
            """);

        Assert.Equal(
            "id:uuid:-:NO, store_id:uuid:-:NO, name:character varying:100:NO, description:character varying:1000:NO, price:numeric:12,2:NO, archived_at:timestamp with time zone:-:YES, version:integer:32,0:NO",
            columns);
    }

    [Fact]
    public async Task TheNameIsUniqueAmongTheLiveProductsOfAStore_AsAPartialIndex()
    {
        var database = await MigratedDatabaseAsync();

        var index = await PostgresServer.ScalarAsync<string>(
            database, "select indexdef from pg_indexes where schemaname = 'public' and indexname = 'ix_products_store_id_name'");

        Assert.Equal(
            "CREATE UNIQUE INDEX ix_products_store_id_name ON public.products USING btree (store_id, name) WHERE (archived_at IS NULL)", index);
    }

    [Fact]
    public async Task TwoLiveProductsWithOneNameInOneStore_AreRefused_ButAnotherStoreMayUseTheName()
    {
        var (database, storeId) = await WithAStoreAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Green Basket', 'y')");
        var other = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores where name = 'Green Basket'");
        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk"));
        await PostgresServer.ExecuteAsync(database, Insert(other, "Milk")); // another store: fine

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk")));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_products_store_id_name", exception.ConstraintName);
    }

    [Fact]
    public async Task AnArchivedProduct_FreesItsName_AndManyArchivedOnesMayShareOne()
    {
        var (database, storeId) = await WithAStoreAsync();
        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk", archivedAt: "2026-01-01T00:00:00Z"));
        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk", archivedAt: "2026-02-01T00:00:00Z"));

        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk")); // a live one with the same name

        Assert.Equal(3L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from products where name = 'Milk'"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1.00")]
    public async Task APriceThatIsNotPositive_IsRefused(string price)
    {
        var (database, storeId) = await WithAStoreAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk", price)));

        Assert.Equal("ck_products_price_is_positive", exception.ConstraintName);
    }

    [Fact]
    public async Task APriceKeepsItsCents_AndATooBigOneIsRefused()
    {
        var (database, storeId) = await WithAStoreAsync();
        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk", "1.10"));

        var tooBig = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, Insert(storeId, "Gold", "10000000000.00")));

        Assert.Equal("1.10", await PostgresServer.ScalarAsync<string>(database, "select price::text from products"));
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, tooBig.SqlState);
    }

    [Fact]
    public async Task AProductNeedsARealStore_AndAStoreWithProductsCannotBeDeleted()
    {
        var (database, storeId) = await WithAStoreAsync();

        var noStore = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, Insert(Guid.CreateVersion7(), "Milk")));
        await PostgresServer.ExecuteAsync(database, Insert(storeId, "Milk"));
        var inUse = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, $"delete from stores where id = '{storeId}'"));

        Assert.Equal("fk_products_stores_store_id", noStore.ConstraintName);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, inUse.SqlState);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused_AndALongNameOrDescriptionToo()
    {
        var (database, storeId) = await WithAStoreAsync();

        var id = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into products (id, store_id, name, description, price) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', '{storeId}', 'Milk', '', 1)"));
        var name = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, Insert(storeId, new string('a', Product.MaxNameLength + 1))));
        var description = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '{new string('a', Product.MaxDescriptionLength + 1)}', 1)"));

        Assert.Equal("ck_products_id_is_uuid_v7", id.ConstraintName);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, name.SqlState);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, description.SqlState);
    }

    [Fact]
    public async Task TheProductPermissions_AreSeededForTheRolesThatHoldThem()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name, c.claim_value)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value like 'products.%'
            """);

        Assert.Equal("Admin=products.read, Customer=products.read, StoreUser=products.manage, StoreUser=products.read", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTableAndItsPermissions_ButKeepsTheStores()
    {
        var (database, _) = await WithAStoreAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddProducts", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.products')::text"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value like 'products.%'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from stores"));
    }
}
