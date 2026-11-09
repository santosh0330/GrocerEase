using Npgsql;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The least-privilege setup from docker/postgres/init/01-roles.sh: the API must not connect as something that can
/// destroy or reshape the database. Each test creates its own uniquely named table, so tests cannot disturb each other.
/// </summary>
public class DatabaseRolesTests
{
    private static string UniqueTable() => $"probe_{Guid.NewGuid():N}";

    [Fact]
    public async Task ThePostgresContainerRunsAsANonRootUser()
    {
        var container = await PostgresServer.ContainerAsync();

        var result = await container.ExecAsync(["id", "-u"]);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEqual("0", result.Stdout.Trim(), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(PostgresServer.AppUser)]
    [InlineData(PostgresServer.MigratorUser)]
    public async Task TheApplicationRoles_AreNotPowerfulRoles(string role)
    {
        var admin = await PostgresServer.ConnectionStringAsync(PostgresServer.AdminUser, PostgresServer.AdminPassword);

        var flags = await PostgresServer.ScalarAsync<string>(
            admin,
            $"select concat_ws(',', rolsuper::text, rolcreaterole::text, rolcreatedb::text, rolreplication::text, rolbypassrls::text) from pg_roles where rolname = '{role}'");

        Assert.Equal("false,false,false,false,false", flags);
    }

    [Fact]
    public async Task TheMigratorCanCreateTables_AndOwnsThem()
    {
        var migrator = await PostgresServer.MigratorConnectionStringAsync();
        var table = UniqueTable();

        await PostgresServer.ExecuteAsync(migrator, $"create table {table} (id int primary key)");

        Assert.Equal(PostgresServer.MigratorUser, await PostgresServer.ScalarAsync<string>(migrator, $"select tableowner from pg_tables where tablename = '{table}'"));
        await PostgresServer.ExecuteAsync(migrator, $"drop table {table}");
    }

    [Fact]
    public async Task TheAppRoleCanReadAndWriteRowsInTablesTheMigratorCreated()
    {
        var migrator = await PostgresServer.MigratorConnectionStringAsync();
        var app = await PostgresServer.AppConnectionStringAsync();
        var table = UniqueTable();
        await PostgresServer.ExecuteAsync(migrator, $"create table {table} (id int primary key, name text)");

        await PostgresServer.ExecuteAsync(app, $"insert into {table} values (1, 'a')");
        await PostgresServer.ExecuteAsync(app, $"update {table} set name = 'b' where id = 1");
        Assert.Equal("b", await PostgresServer.ScalarAsync<string>(app, $"select name from {table} where id = 1"));
        await PostgresServer.ExecuteAsync(app, $"delete from {table} where id = 1");

        await PostgresServer.ExecuteAsync(migrator, $"drop table {table}");
    }

    [Theory]
    [InlineData("create table {0}_new (id int)")]
    [InlineData("alter table {0} add column extra int")]
    [InlineData("drop table {0}")]
    [InlineData("truncate table {0}")]
    [InlineData("create schema {0}_schema")]
    [InlineData("create role {0}_role")]
    [InlineData("create database {0}_db")]
    public async Task TheAppRoleCannotCreateAlterOrDropAnything(string statementTemplate)
    {
        var migrator = await PostgresServer.MigratorConnectionStringAsync();
        var app = await PostgresServer.AppConnectionStringAsync();
        var table = UniqueTable();
        await PostgresServer.ExecuteAsync(migrator, $"create table {table} (id int primary key)");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(app, string.Format(System.Globalization.CultureInfo.InvariantCulture, statementTemplate, table)));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        await PostgresServer.ExecuteAsync(migrator, $"drop table {table}");
    }

    [Fact]
    public async Task TheAppRoleCannotSeeTheMigrationHistory_BecauseItLivesInAnotherSchema()
    {
        var migrator = await PostgresServer.MigratorConnectionStringAsync();
        var app = await PostgresServer.AppConnectionStringAsync();
        await PostgresServer.ExecuteAsync(migrator, "create schema if not exists migrations");
        await PostgresServer.ExecuteAsync(migrator, "create table if not exists migrations.history_probe (id int)");

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresServer.ExecuteAsync(app, "select * from migrations.history_probe"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }

    [Fact]
    public async Task TheAppRoleCannotConnectToTheDefaultDatabase()
    {
        var app = await PostgresServer.ConnectionStringAsync(PostgresServer.AppUser, PostgresServer.AppPassword, "postgres");

        var exception = await Assert.ThrowsAsync<PostgresException>(() => PostgresServer.ExecuteAsync(app, "select 1"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
