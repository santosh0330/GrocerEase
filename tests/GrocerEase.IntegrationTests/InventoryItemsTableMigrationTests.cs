using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddInventoryItems migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class InventoryItemsTableMigrationTests
{
    private static async Task<(string Database, Guid StoreId, Guid ProductId)> WithAProductAsync()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        var storeId = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores");
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 1)");
        return (database, storeId, await PostgresServer.ScalarAsync<Guid>(database, "select id from products"));
    }

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'inventory_items'
            """);

        Assert.Equal("product_id:uuid:NO, store_id:uuid:NO, quantity_on_hand:integer:NO", columns);
    }

    [Fact]
    public async Task TheProductIsThePrimaryKey_SoThereIsOneRowPerProduct()
    {
        var (database, storeId, productId) = await WithAProductAsync();
        await PostgresServer.ExecuteAsync(database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', 5)");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', 9)"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("pk_inventory_items", exception.ConstraintName);
    }

    [Fact]
    public async Task TheStoreOfAnItem_MustBeTheStoreOfItsProduct_BecauseOfTheCompositeForeignKey()
    {
        var (database, _, productId) = await WithAProductAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Green Basket', 'y')");
        var other = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores where name = 'Green Basket'");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{other}', 5)"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("fk_inventory_items_products_product_id_store_id", exception.ConstraintName);
    }

    [Fact]
    public async Task AnItemNeedsARealProduct_AndAProductWithStockCannotBeDeleted()
    {
        var (database, storeId, productId) = await WithAProductAsync();

        var noProduct = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{Guid.CreateVersion7()}', '{storeId}', 1)"));
        await PostgresServer.ExecuteAsync(database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', 1)");
        var inUse = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, $"delete from products where id = '{productId}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, noProduct.SqlState);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, inUse.SqlState);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1000000001")]
    public async Task AQuantityOutsideZeroToTheMaximum_IsRefusedByTheTable(string quantity)
    {
        var (database, storeId, productId) = await WithAProductAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', {quantity})"));

        Assert.Equal("ck_inventory_items_quantity_in_range", exception.ConstraintName);
    }

    [Fact]
    public async Task TheEdgesOfTheRange_AreAccepted()
    {
        var (database, storeId, productId) = await WithAProductAsync();

        await PostgresServer.ExecuteAsync(database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', 0)");
        await PostgresServer.ExecuteAsync(database, $"update inventory_items set quantity_on_hand = 1000000000 where product_id = '{productId}'");

        Assert.Equal(1_000_000_000, await PostgresServer.ScalarAsync<int>(database, "select quantity_on_hand from inventory_items"));
    }

    [Fact]
    public async Task TheStoreHasAnIndex_AndEveryIndexIsNamed()
    {
        var database = await MigratedDatabaseAsync();

        var indexes = await PostgresServer.ScalarAsync<string>(
            database, "select string_agg(indexname, ', ' order by indexname) from pg_indexes where schemaname = 'public' and tablename = 'inventory_items'");

        Assert.Equal("ix_inventory_items_product_id_store_id, ix_inventory_items_store_id, pk_inventory_items", indexes);
    }

    [Fact]
    public async Task TheInventoryPermission_IsSeededForTheStoreUserOnly()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value = 'inventory.manage'
            """);

        Assert.Equal("StoreUser=inventory.manage", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTableTheConstraintAndThePermission_ButKeepsTheProducts()
    {
        var (database, _, _) = await WithAProductAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddInventoryItems", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.inventory_items')::text"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from pg_constraint where conname = 'uq_products_id_store_id'"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'inventory.manage'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from products"));
    }
}
