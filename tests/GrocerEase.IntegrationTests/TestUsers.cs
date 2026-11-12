using GrocerEase.Domain.Entities;

namespace GrocerEase.IntegrationTests;

/// <summary>Changes a user in the test database in ways the application cannot yet.</summary>
internal static class TestUsers
{
    /// <summary>
    /// Gives the user the Admin role, by SQL: nothing in the application changes a role yet, so this is how the first admin is made.
    /// A token the user already holds keeps saying Customer; they need a new one (a login or a refresh).
    /// </summary>
    public static async Task MakeAdminAsync(string email) =>
        await PostgresServer.ExecuteAsync(
            await PostgresServer.ConnectionStringAsync(PostgresServer.AdminUser, PostgresServer.AdminPassword),
            $"update users set role_id = '{RoleIds.Admin}' where email = '{email}'");
}
