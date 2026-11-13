using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddLowStockThreshold migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class LowStockThresholdMigrationTests
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
    public async Task TheColumn_IsNullable_SoARowWithNoLevelIsValid_AndTheOldRowsGetNone()
    {
        var (database, storeId, productId) = await WithAProductAsync();
        await PostgresServer.ExecuteAsync(database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{productId}', '{storeId}', 5)");

        Assert.Null(await PostgresServer.ScalarAsync<int?>(database, "select low_stock_threshold from inventory_items"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("1000000000")]
    [InlineData("null")]
    public async Task ALevelInRange_AndNoLevel_AreAccepted(string level)
    {
        var (database, storeId, productId) = await WithAProductAsync();

        await PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand, low_stock_threshold) values ('{productId}', '{storeId}', 5, {level})");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1000000001")]
    public async Task ALevelOutOfRange_IsRefusedByTheDatabase_WhateverWroteIt(string level)
    {
        var (database, storeId, productId) = await WithAProductAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand, low_stock_threshold) values ('{productId}', '{storeId}', 5, {level})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_inventory_items_low_stock_threshold_in_range", exception.ConstraintName);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheColumnAndItsConstraint_ButKeepsTheStock()
    {
        var (database, storeId, productId) = await WithAProductAsync();
        await PostgresServer.ExecuteAsync(
            database, $"insert into inventory_items (product_id, store_id, quantity_on_hand, low_stock_threshold) values ('{productId}', '{storeId}', 5, 3)");
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddLowStockThreshold", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from information_schema.columns where table_name = 'inventory_items' and column_name = 'low_stock_threshold'"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from pg_constraint where conname = 'ck_inventory_items_low_stock_threshold_in_range'"));
        Assert.Equal(5, await PostgresServer.ScalarAsync<int>(database, "select quantity_on_hand from inventory_items"));
    }
}
