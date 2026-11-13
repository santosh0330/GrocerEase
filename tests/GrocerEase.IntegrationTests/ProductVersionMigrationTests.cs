using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddProductVersion migration, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class ProductVersionMigrationTests
{
    private static async Task<(string Database, Guid StoreId)> WithAStoreAsync(string? database = null)
    {
        database ??= await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        return (database, await PostgresServer.ScalarAsync<Guid>(database, "select id from stores"));
    }

    [Fact]
    public async Task ANewProductWithNoVersionGiven_IsVersionOne_ByTheColumnsDefault()
    {
        var (database, storeId) = await WithAStoreAsync();

        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 1)");

        Assert.Equal(1, await PostgresServer.ScalarAsync<int>(database, "select version from products"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AVersionBelowOne_IsRefusedByTheDatabase_WhateverWroteIt(int version)
    {
        var (database, storeId) = await WithAStoreAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into products (store_id, name, description, price, version) values ('{storeId}', 'Milk', '', 1, {version})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_products_version_is_positive", exception.ConstraintName);
    }

    [Fact]
    public async Task ProductsThatExistedBeforeTheColumn_AreVersionOne_ArchivedOnesToo()
    {
        var database = await PostgresServer.CreateEmptyDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddProductVersion", StringComparison.Ordinal));
        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);
        var (_, storeId) = await WithAStoreAsync(database);
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 1)");
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price, archived_at) values ('{storeId}', 'Old', '', 1, now())");

        await MigrateAsync(database);

        Assert.Equal(
            "Milk:1, Old:1",
            await PostgresServer.ScalarAsync<string>(database, "select string_agg(name || ':' || version, ', ' order by name) from products"));
    }

    [Fact]
    public async Task RollingBackThisMigration_DropsTheColumnAndItsConstraint_ButKeepsTheProducts()
    {
        var (database, storeId) = await WithAStoreAsync();
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price, version) values ('{storeId}', 'Milk', '', 1, 5)");
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddProductVersion", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from information_schema.columns where table_name = 'products' and column_name = 'version'"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from pg_constraint where conname = 'ck_products_version_is_positive'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from products"));
    }
}
