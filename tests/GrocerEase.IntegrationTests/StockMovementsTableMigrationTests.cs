using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddStockMovements migration, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class StockMovementsTableMigrationTests
{
    private static readonly string[] RefusingChecks = ["ck_stock_movements_reason_is_known", "ck_stock_movements_delta_matches_reason"];

    private sealed record World(string Database, Guid StoreId, Guid ProductId, Guid UserId);

    private static async Task<World> AWorldAsync(string? database = null)
    {
        database ??= await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        var storeId = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores");
        await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 1)");
        await PostgresServer.ExecuteAsync(
            database, "insert into users (first_name, last_name, email, password_hash, role_id) values ('Sam', 'R', 'sam@example.com', 'x', '019e0000-0000-7000-8000-000000000003')");
        return new World(
            database, storeId, await PostgresServer.ScalarAsync<Guid>(database, "select id from products"), await PostgresServer.ScalarAsync<Guid>(database, "select id from users"));
    }

    private static string Insert(World world, int delta, string reason, string? actor, string? order) =>
        $"insert into stock_movements (product_id, store_id, delta, reason, actor_id, order_id, created_at) values ('{world.ProductId}', '{world.StoreId}', {delta}, '{reason}', "
        + $"{(actor is null ? "null" : $"'{actor}'")}, {(order is null ? "null" : $"'{order}'")}, now())";

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '-') || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'stock_movements'
            """);

        Assert.Equal(
            "id:uuid:-:NO, product_id:uuid:-:NO, store_id:uuid:-:NO, delta:integer:-:NO, reason:character varying:20:NO, actor_id:uuid:-:YES, order_id:uuid:-:YES, created_at:timestamp with time zone:-:NO",
            columns);
    }

    [Fact]
    public async Task EveryIndexIsNamed_AndTheHistoryIsInTheOrderItIsAskedFor()
    {
        var database = await MigratedDatabaseAsync();

        var indexes = await PostgresServer.ScalarAsync<string>(
            database,
            "select string_agg(indexname || ': ' || substring(indexdef from 'USING .*'), E'\\n' order by indexname) from pg_indexes where schemaname = 'public' and tablename = 'stock_movements'");

        Assert.Equal(
            "ix_stock_movements_actor_id: USING btree (actor_id)\n"
            + "ix_stock_movements_order_id: USING btree (order_id)\n"
            + "ix_stock_movements_product_id_created_at: USING btree (product_id, created_at DESC, id DESC)\n"
            + "ix_stock_movements_product_id_store_id: USING btree (product_id, store_id)\n"
            + "pk_stock_movements: USING btree (id)",
            indexes);
    }

    [Fact]
    public async Task ALegalMovementOfEachReason_IsAccepted_AndTheIdDefaultsToAVersion7Uuid()
    {
        var world = await AWorldAsync();
        var u = world.UserId.ToString();

        await PostgresServer.ExecuteAsync(world.Database, Insert(world, 5, "OpeningBalance", null, null));
        await PostgresServer.ExecuteAsync(world.Database, Insert(world, 5, "Received", u, null));
        await PostgresServer.ExecuteAsync(world.Database, Insert(world, -2, "Correction", u, null));
        await PostgresServer.ExecuteAsync(world.Database, Insert(world, 2, "Correction", u, null));

        Assert.Equal((4L, 4L), (
            await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from stock_movements"),
            await PostgresServer.ScalarAsync<long>(world.Database, "select count(*) from stock_movements where uuid_extract_version(id) = 7")));
    }

    [Theory]
    [InlineData(0, "Received", "actor", null, "ck_stock_movements_delta_in_range")] // a change of nothing
    [InlineData(-1, "Received", "actor", null, "ck_stock_movements_delta_matches_reason")] // a delivery adds
    [InlineData(1, "OrderPlaced", "actor", "order", "ck_stock_movements_delta_matches_reason")] // an order removes
    [InlineData(-1, "OrderCancelled", "actor", "order", "ck_stock_movements_delta_matches_reason")]
    [InlineData(-1, "OpeningBalance", null, null, "ck_stock_movements_delta_matches_reason")]
    [InlineData(1_000_000_001, "Received", "actor", null, "ck_stock_movements_delta_in_range")]
    [InlineData(-1, "OrderPlaced", "actor", null, "ck_stock_movements_order_id_matches_reason")] // an order's movement says which order
    [InlineData(1, "Received", "actor", "order", "ck_stock_movements_order_id_matches_reason")] // and nothing else does
    [InlineData(1, "Received", null, null, "ck_stock_movements_actor_id_matches_reason")] // every movement but the opening balance has an actor
    [InlineData(1, "OpeningBalance", "actor", null, "ck_stock_movements_actor_id_matches_reason")]
    public async Task ARowThatMakesNoSense_IsRefusedByTheDatabase_WhateverWroteIt(int delta, string reason, string? actor, string? order, string constraint)
    {
        var world = await AWorldAsync();
        // An order of its own, so that only the check under test can refuse the row.
        Guid? orderId = order is null ? null : await AnOrderAsync(world);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            world.Database, Insert(world, delta, reason, actor is null ? null : world.UserId.ToString(), orderId?.ToString())));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    [Fact]
    public async Task AnUnknownReason_IsRefusedByTheDatabase()
    {
        var world = await AWorldAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Insert(world, 1, "Lost", world.UserId.ToString(), null)));

        // Two checks refuse it (the list of reasons, and the direction each reason allows); PostgreSQL names the first it evaluates.
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains(exception.ConstraintName, RefusingChecks, StringComparer.Ordinal);
    }

    private static async Task<Guid> AnOrderAsync(World world)
    {
        await PostgresServer.ExecuteAsync(
            world.Database, $"insert into orders (store_id, customer_id, status, delivery_address, placed_at) values ('{world.StoreId}', '{world.UserId}', 'Pending', 'x', now())");
        return await PostgresServer.ScalarAsync<Guid>(world.Database, "select id from orders");
    }

    [Fact]
    public async Task TheStoreOfAMovement_MustBeTheStoreOfItsProduct_BecauseOfTheCompositeForeignKey()
    {
        var world = await AWorldAsync();
        await PostgresServer.ExecuteAsync(world.Database, "insert into stores (name, address) values ('Green Basket', 'y')");
        var other = await PostgresServer.ScalarAsync<Guid>(world.Database, "select id from stores where name = 'Green Basket'");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            world.Database,
            $"insert into stock_movements (product_id, store_id, delta, reason, actor_id, created_at) values ('{world.ProductId}', '{other}', 1, 'Received', '{world.UserId}', now())"));

        Assert.Equal("fk_stock_movements_products_product_id_store_id", exception.ConstraintName);
    }

    [Fact]
    public async Task TheActorMustBeARealUser_AndTheOrderARealOrder()
    {
        var world = await AWorldAsync();

        var noActor = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Insert(world, 1, "Received", Guid.CreateVersion7().ToString(), null)));
        var noOrder = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(world.Database, Insert(world, -1, "OrderPlaced", world.UserId.ToString(), Guid.CreateVersion7().ToString())));

        Assert.Equal("fk_stock_movements_users_actor_id", noActor.ConstraintName);
        Assert.Equal("fk_stock_movements_orders_order_id", noOrder.ConstraintName);
    }

    [Fact]
    public async Task TheOrdersForeignKey_IsCheckedAtCommit_SoAMovementMayBeWrittenBeforeItsOrderInTheSameTransaction()
    {
        var world = await AWorldAsync();
        var orderId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(world.Database);
        await connection.OpenAsync();

        // The order's stock is taken first and the order is written after, as placing an order does.
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await new NpgsqlCommand(Insert(world, -1, "OrderPlaced", world.UserId.ToString(), orderId.ToString()), connection, transaction).ExecuteNonQueryAsync();
            await new NpgsqlCommand(
                $"insert into orders (id, store_id, customer_id, status, delivery_address, placed_at) values ('{orderId}', '{world.StoreId}', '{world.UserId}', 'Pending', 'x', now())",
                connection, transaction).ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(world.Database, $"select count(*) from stock_movements where order_id = '{orderId}'"));

        // ...and if the order never arrives, the commit is refused, and the movement with it.
        var missing = Guid.CreateVersion7();
        await using var transaction2 = await connection.BeginTransactionAsync();
        await new NpgsqlCommand(Insert(world, -1, "OrderPlaced", world.UserId.ToString(), missing.ToString()), connection, transaction2).ExecuteNonQueryAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction2.CommitAsync());
        Assert.Equal("fk_stock_movements_orders_order_id", exception.ConstraintName);
    }

    [Fact]
    public async Task EveryProductThatHadStock_GetsOneOpeningBalance_ACountOfZeroGetsNone_AndTheLedgerAddsUpFromTheStart()
    {
        // Migrate to the step before the ledger, put stock in, then migrate up: the migration must explain what is already there.
        var database = await PostgresServer.CreateEmptyDatabaseAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddStockMovements", StringComparison.Ordinal));
        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        var store = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores");
        foreach (var (name, quantity) in new[] { ("Milk", 7), ("Bread", 0), ("Eggs", 1_000_000_000) })
        {
            await PostgresServer.ExecuteAsync(database, $"insert into products (store_id, name, description, price) values ('{store}', '{name}', '', 1)");
            var id = await PostgresServer.ScalarAsync<Guid>(database, $"select id from products where name = '{name}'");
            await PostgresServer.ExecuteAsync(database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{id}', '{store}', {quantity})");
        }

        await MigrateAsync(database);

        Assert.Equal(
            "Eggs:OpeningBalance:1000000000, Milk:OpeningBalance:7",
            await PostgresServer.ScalarAsync<string>(
                database,
                "select string_agg(p.name || ':' || m.reason || ':' || m.delta, ', ' order by p.name) from stock_movements m join products p on p.id = m.product_id"));
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from stock_movements where actor_id is not null or order_id is not null"));
        // The ledger adds up for every product: the sum of its deltas is its quantity.
        Assert.Equal(0L, await PostgresServer.ScalarAsync<long>(
            database,
            """
            select count(*) from inventory_items i
            where i.quantity_on_hand <> coalesce((select sum(delta) from stock_movements m where m.product_id = i.product_id), 0)
            """));
    }

    [Fact]
    public async Task TheApplicationRole_MayAddAndReadMovements_ButNotChangeOrDeleteOne()
    {
        // The application database, migrated by the migrator and used by the API as the least-privilege role.
        var app = await PostgresServer.ApplicationConnectionStringAsync();
        var store = new { Name = $"Store {Guid.NewGuid():N}" };
        await PostgresServer.ExecuteAsync(app, $"insert into stores (name, address) values ('{store.Name}', 'x')");
        var storeId = await PostgresServer.ScalarAsync<Guid>(app, $"select id from stores where name = '{store.Name}'");
        await PostgresServer.ExecuteAsync(app, $"insert into products (store_id, name, description, price) values ('{storeId}', 'Milk', '', 1)");
        var productId = await PostgresServer.ScalarAsync<Guid>(app, $"select id from products where store_id = '{storeId}'");
        var email = $"sam-{Guid.NewGuid():N}@example.com";
        await PostgresServer.ExecuteAsync(app, $"insert into users (first_name, last_name, email, password_hash, role_id) values ('Sam', 'R', '{email}', 'x', '019e0000-0000-7000-8000-000000000003')");
        var userId = await PostgresServer.ScalarAsync<Guid>(app, $"select id from users where email = '{email}'");

        await PostgresServer.ExecuteAsync(
            app, $"insert into stock_movements (product_id, store_id, delta, reason, actor_id, created_at) values ('{productId}', '{storeId}', 5, 'Received', '{userId}', now())");
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(app, $"select count(*) from stock_movements where product_id = '{productId}'"));

        var update = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(app, $"update stock_movements set delta = 99 where product_id = '{productId}'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(app, $"delete from stock_movements where product_id = '{productId}'"));
        var truncate = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(app, "truncate stock_movements"));

        Assert.Equal(
            (PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.InsufficientPrivilege, PostgresErrorCodes.InsufficientPrivilege),
            (update.SqlState, delete.SqlState, truncate.SqlState));
        Assert.Equal(5, await PostgresServer.ScalarAsync<int>(app, $"select delta from stock_movements where product_id = '{productId}'")); // history stands
    }

    [Fact]
    public async Task RollingBackThisMigration_DropsTheLedger_ButKeepsTheStock()
    {
        var world = await AWorldAsync();
        await PostgresServer.ExecuteAsync(world.Database, $"insert into inventory_items (product_id, store_id, quantity_on_hand) values ('{world.ProductId}', '{world.StoreId}', 5)");
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddStockMovements", StringComparison.Ordinal));

        await MigrateAsync(world.Database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(world.Database, "select to_regclass('public.stock_movements')::text"));
        Assert.Equal(5, await PostgresServer.ScalarAsync<int>(world.Database, "select quantity_on_hand from inventory_items"));
    }
}
