using GrocerEase.Domain.Entities;
using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddOrders migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class OrdersTableMigrationTests
{
    private sealed record World(string Database, Guid StoreId, Guid CustomerId, Guid ProductId);

    private static async Task<World> AWorldAsync()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        var storeId = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores");
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 2.5)");
        await PostgresServer.ExecuteAsync(
            database, "insert into users (first_name, last_name, email, password_hash, role_id) values ('Ada', 'L', 'ada@example.com', 'x', '019e0000-0000-7000-8000-000000000001')");
        return new World(
            database, storeId, await PostgresServer.ScalarAsync<Guid>(database, "select id from users"), await PostgresServer.ScalarAsync<Guid>(database, "select id from products"));
    }

    private static Task InsertOrderAsync(World world, string status = "Pending") =>
        PostgresServer.ExecuteAsync(world.Database, $"insert into orders (store_id, customer_id, status, delivery_address, placed_at) values ('{world.StoreId}', '{world.CustomerId}', '{status}', '1 High Street', now())");

    [Fact]
    public async Task MigratingCreatesBothTablesWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        string Columns(string table) => $"""
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, coalesce(numeric_precision::text || ',' || numeric_scale::text, '-')) || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = '{table}'
            """;

        Assert.Equal(
            "id:uuid:-:NO, store_id:uuid:-:NO, customer_id:uuid:-:NO, status:character varying:20:NO, delivery_address:character varying:300:NO, placed_at:timestamp with time zone:-:NO",
            await PostgresServer.ScalarAsync<string>(database, Columns("orders")));
        Assert.Equal(
            "id:uuid:-:NO, order_id:uuid:-:NO, product_id:uuid:-:NO, product_name:character varying:100:NO, unit_price:numeric:12,2:NO, quantity:integer:32,0:NO",
            await PostgresServer.ScalarAsync<string>(database, Columns("order_lines")));
    }

    [Fact]
    public async Task EveryIndexIsNamed_AndTheListIsInTheOrderItIsAskedFor()
    {
        var database = await MigratedDatabaseAsync();

        var indexes = await PostgresServer.ScalarAsync<string>(
            database,
            "select string_agg(indexname || ': ' || substring(indexdef from 'USING .*'), E'\\n' order by indexname) from pg_indexes where schemaname = 'public' and tablename in ('orders', 'order_lines')");

        Assert.Equal(
            "ix_order_lines_order_id_product_id: USING btree (order_id, product_id)\n"
            + "ix_order_lines_product_id: USING btree (product_id)\n"
            + "ix_orders_customer_id_placed_at: USING btree (customer_id, placed_at DESC, id DESC)\n"
            + "ix_orders_store_id_placed_at: USING btree (store_id, placed_at, id)\n"
            + "pk_order_lines: USING btree (id)\n"
            + "pk_orders: USING btree (id)",
            indexes);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Verified")]
    [InlineData("Shipped")]
    [InlineData("Cancelled")]
    public async Task EveryStatusTheEnumHas_IsAccepted(string status)
    {
        var world = await AWorldAsync();

        await InsertOrderAsync(world, status);

        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(world.Database, $"select count(*) from orders where status = '{status}'"));
    }

    [Fact]
    public async Task AStatusTheEnumDoesNotHave_IsRefused()
    {
        var world = await AWorldAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertOrderAsync(world, "Delivered"));

        Assert.Equal("ck_orders_status_is_known", exception.ConstraintName);
    }

    [Fact]
    public async Task AnOrderNeedsARealStoreAndARealCustomer_AndNeitherCanBeDeletedWhileItHasOrders()
    {
        var world = await AWorldAsync();

        var noStore = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            world.Database, $"insert into orders (store_id, customer_id, status, delivery_address, placed_at) values ('{Guid.CreateVersion7()}', '{world.CustomerId}', 'Pending', 'x', now())"));
        var noCustomer = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            world.Database, $"insert into orders (store_id, customer_id, status, delivery_address, placed_at) values ('{world.StoreId}', '{Guid.CreateVersion7()}', 'Pending', 'x', now())"));
        await InsertOrderAsync(world);
        var storeInUse = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, $"delete from stores where id = '{world.StoreId}'"));
        var customerInUse = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, $"delete from users where id = '{world.CustomerId}'"));

        Assert.Equal("fk_orders_stores_store_id", noStore.ConstraintName);
        Assert.Equal("fk_orders_users_customer_id", noCustomer.ConstraintName);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, storeInUse.SqlState);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, customerInUse.SqlState);
    }

    [Fact]
    public async Task ALineIsAProductAsItWasBought_OneLinePerProduct_AndItsPriceAndQuantityHaveLimits()
    {
        var world = await AWorldAsync();
        await InsertOrderAsync(world);
        var orderId = await PostgresServer.ScalarAsync<Guid>(world.Database, "select id from orders");
        string Line(string price = "2.5", string quantity = "3") =>
            $"insert into order_lines (order_id, product_id, product_name, unit_price, quantity) values ('{orderId}', '{world.ProductId}', 'Milk', {price}, {quantity})";
        await PostgresServer.ExecuteAsync(world.Database, Line());

        var twice = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Line()));
        var price = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Line(price: "0")));
        var tooFew = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Line(quantity: "0")));
        var tooMany = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Line(quantity: "1000000001")));

        Assert.Equal("ix_order_lines_order_id_product_id", twice.ConstraintName);
        Assert.Equal("ck_order_lines_unit_price_is_positive", price.ConstraintName);
        Assert.Equal("ck_order_lines_quantity_in_range", tooFew.ConstraintName);
        Assert.Equal("ck_order_lines_quantity_in_range", tooMany.ConstraintName);
    }

    [Fact]
    public async Task AProductThatWasOrdered_CannotBeDeleted_AndDeletingAnOrderDeletesItsLines()
    {
        var world = await AWorldAsync();
        await InsertOrderAsync(world);
        var orderId = await PostgresServer.ScalarAsync<Guid>(world.Database, "select id from orders");
        await PostgresServer.ExecuteAsync(world.Database, $"insert into order_lines (order_id, product_id, product_name, unit_price, quantity) values ('{orderId}', '{world.ProductId}', 'Milk', 2.5, 3)");

        var inUse = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, $"delete from products where id = '{world.ProductId}'"));
        await PostgresServer.ExecuteAsync(world.Database, $"delete from orders where id = '{orderId}'");

        Assert.Equal(PostgresErrorCodes.RestrictViolation, inUse.SqlState);
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from order_lines"));
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused_ForAnOrderAndForALine()
    {
        var world = await AWorldAsync();

        var order = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            world.Database, $"insert into orders (id, store_id, customer_id, status, delivery_address, placed_at) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', '{world.StoreId}', '{world.CustomerId}', 'Pending', 'x', now())"));

        Assert.Equal("ck_orders_id_is_uuid_v7", order.ConstraintName);
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesBothTables_ButKeepsTheStoresProductsAndUsers()
    {
        var world = await AWorldAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddOrders", StringComparison.Ordinal));

        await MigrateAsync(world.Database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(world.Database, "select to_regclass('public.orders')::text"));
        Assert.Null(await PostgresServer.ScalarAsync<string>(world.Database, "select to_regclass('public.order_lines')::text"));
        Assert.Equal((1L, 1L, 1L), (
            await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from stores"),
            await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from products"),
            await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from users")));
    }

    [Fact]
    public async Task RollingBackTheIndexMigration_PutsTheOldStoreIndexBack_AndKeepsTheOrders()
    {
        var world = await AWorldAsync();
        await InsertOrderAsync(world);
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_IndexOrdersByStoreAndTime", StringComparison.Ordinal));

        await MigrateAsync(world.Database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Equal(
            "ix_orders_store_id",
            await PostgresServer.ScalarAsync<string>(world.Database, "select string_agg(indexname, ', ') from pg_indexes where tablename = 'orders' and indexname like 'ix_orders_store%'"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from orders"));
    }
}
