using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The SeedOrdersReadAllPermission migration: the admin may read the orders of every store, and no other role may.</summary>
public class OrdersReadAllPermissionMigrationTests
{
    [Fact]
    public async Task TheReadAllPermission_IsSeededForTheAdminOnly()
    {
        var database = await MigratedDatabaseAsync();

        var granted = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(r.name || '=' || c.claim_value, ', ' order by r.name)
            from role_claims c join roles r on r.id = c.role_id where c.claim_value = 'orders.read.all'
            """);

        Assert.Equal("Admin=orders.read.all", granted);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesThePermission_ButKeepsTheOthers()
    {
        var database = await MigratedDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_SeedOrdersReadAllPermission", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.read.all'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'orders.cancel'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from role_claims where claim_value = 'stores.manage'"));
    }
}
