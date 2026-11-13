using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The SeedOrdersCancelPermission migration: the customer may cancel their own order, and no other role may.</summary>
public class OrdersCancelPermissionMigrationTests
{
    [Fact]
    public async Task TheCancelPermission_IsSeededForTheCustomerOnly()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value = 'orders.cancel'
            """);

        Assert.Equal("Customer=orders.cancel", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesThePermission_ButKeepsTheOthers()
    {
        var database = await MigratedDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_SeedOrdersCancelPermission", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.cancel'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.manage'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'inventory.manage'"));
    }
}
