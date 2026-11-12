using Npgsql;
using static GrocerEase.IntegrationTests.MigrationTestSupport;

namespace GrocerEase.IntegrationTests;

/// <summary>The AddStoreMembers migration on a migrated database, checked against the real schema. Raw SQL on purpose: these tests describe the database, not what EF thinks of it.</summary>
public class StoreMembersTableMigrationTests
{
    // The Customer role's seeded id.
    private static string InsertUser(string email) =>
        $"insert into users (first_name, last_name, email, password_hash, role_id) values ('Asha', 'Rai', '{email}', 'x', '019e0000-0000-7000-8000-000000000001')";

    private static async Task<(string Database, Guid StoreId, Guid UserId)> WithAStoreAndAUserAsync()
    {
        var database = await MigratedDatabaseAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Fresh Mart', 'x')");
        await PostgresServer.ExecuteAsync(database, InsertUser("asha@example.com"));
        return (
            database,
            await PostgresServer.ScalarAsync<Guid>(database, "select id from stores"),
            await PostgresServer.ScalarAsync<Guid>(database, "select id from users"));
    }

    [Fact]
    public async Task MigratingCreatesTheTableWithTheExpectedColumns()
    {
        var database = await MigratedDatabaseAsync();

        var columns = await PostgresServer.ScalarAsync<string>(
            database,
            """
            select string_agg(column_name || ':' || data_type || ':' || is_nullable, ', ' order by ordinal_position)
            from information_schema.columns where table_schema = 'public' and table_name = 'store_members'
            """);

        Assert.Equal("id:uuid:NO, store_id:uuid:NO, user_id:uuid:NO", columns);
    }

    [Fact]
    public async Task TheIdIsThePrimaryKey_TheUserIsUnique_AndTheStoreHasAnIndex()
    {
        var database = await MigratedDatabaseAsync();

        var indexes = await PostgresServer.ScalarAsync<string>(
            database, "select string_agg(indexname || ': ' || indexdef, E'\\n' order by indexname) from pg_indexes where schemaname = 'public' and tablename = 'store_members'");

        Assert.Equal(
            "ix_store_members_store_id: CREATE INDEX ix_store_members_store_id ON public.store_members USING btree (store_id)\n"
            + "ix_store_members_user_id: CREATE UNIQUE INDEX ix_store_members_user_id ON public.store_members USING btree (user_id)\n"
            + "pk_store_members: CREATE UNIQUE INDEX pk_store_members ON public.store_members USING btree (id)",
            indexes);
    }

    [Fact]
    public async Task AUserCannotRunTwoStores_ButAStoreCanHaveManyUsers()
    {
        var (database, storeId, userId) = await WithAStoreAndAUserAsync();
        await PostgresServer.ExecuteAsync(database, "insert into stores (name, address) values ('Green Basket', 'y')");
        await PostgresServer.ExecuteAsync(database, InsertUser("ben@example.com"));
        var other = await PostgresServer.ScalarAsync<Guid>(database, "select id from stores where name = 'Green Basket'");
        var ben = await PostgresServer.ScalarAsync<Guid>(database, "select id from users where email = 'ben@example.com'");
        await PostgresServer.ExecuteAsync(database, $"insert into store_members (store_id, user_id) values ('{storeId}', '{userId}')");
        await PostgresServer.ExecuteAsync(database, $"insert into store_members (store_id, user_id) values ('{storeId}', '{ben}')"); // two users, one store: fine

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into store_members (store_id, user_id) values ('{other}', '{userId}')")); // one user, a second store

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("ix_store_members_user_id", exception.ConstraintName);
    }

    [Fact]
    public async Task AMembershipNeedsARealStoreAndARealUser()
    {
        var (database, storeId, userId) = await WithAStoreAndAUserAsync();

        var noStore = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into store_members (store_id, user_id) values ('{Guid.CreateVersion7()}', '{userId}')"));
        var noUser = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into store_members (store_id, user_id) values ('{storeId}', '{Guid.CreateVersion7()}')"));

        Assert.Equal("fk_store_members_stores_store_id", noStore.ConstraintName);
        Assert.Equal("fk_store_members_users_user_id", noUser.ConstraintName);
    }

    [Fact]
    public async Task AStoreOrAUserWithAMembership_CannotBeDeleted()
    {
        var (database, storeId, userId) = await WithAStoreAndAUserAsync();
        await PostgresServer.ExecuteAsync(database, $"insert into store_members (store_id, user_id) values ('{storeId}', '{userId}')");

        var store = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, $"delete from stores where id = '{storeId}'"));
        var user = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(database, $"delete from users where id = '{userId}'"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, store.SqlState); // RESTRICT raises its own code, not the generic foreign key one
        Assert.Equal(PostgresErrorCodes.RestrictViolation, user.SqlState);
    }

    [Fact]
    public async Task AnIdThatIsNotVersion7_IsRefused_AndAMissingOneGetsVersion7FromTheDatabase()
    {
        var (database, storeId, userId) = await WithAStoreAndAUserAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(
            database, $"insert into store_members (id, store_id, user_id) values ('7d444840-9dc0-41d3-9d4e-93b7d3b1c5a1', '{storeId}', '{userId}')"));
        await PostgresServer.ExecuteAsync(database, $"insert into store_members (store_id, user_id) values ('{storeId}', '{userId}')");

        Assert.Equal("ck_store_members_id_is_uuid_v7", exception.ConstraintName);
        Assert.Equal(7, await PostgresServer.ScalarAsync<int>(database, "select uuid_extract_version(id)::int from store_members"));
    }

    [Fact]
    public async Task RollingBackThisMigration_RemovesTheTable_ButKeepsTheStoresAndTheUsers()
    {
        var (database, _, _) = await WithAStoreAndAUserAsync();
        var thisMigration = AllMigrations().Single(migration => migration.EndsWith("_AddStoreMembers", StringComparison.Ordinal));

        await MigrateAsync(database, AllMigrations()[AllMigrations().ToList().IndexOf(thisMigration) - 1]);

        Assert.Null(await PostgresServer.ScalarAsync<string>(database, "select to_regclass('public.store_members')::text"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from stores"));
        Assert.Equal(1L, await PostgresServer.ScalarAsync<long>(database, "select count(*) from users"));
    }
}
