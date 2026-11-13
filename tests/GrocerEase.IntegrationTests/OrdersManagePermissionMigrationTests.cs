using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The SeedOrdersManagePermission migration: the store user may move the orders of their store, and no other role may.</summary>
public class OrdersManagePermissionMigrationTests
{
    [Fact]
    public async Task TheOrdersPermission_IsSeededForTheStoreUserOnly()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value = 'orders.manage'
            """);

        Assert.Equal("StoreUser=orders.manage", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesThePermission_ButKeepsTheOthers()
    {
        var database = await MigratedDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_SeedOrdersManagePermission", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.manage'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.place'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'inventory.manage'"));
    }
}
