using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Authentication.Common;

/// <summary>
/// Reads a role that must exist. Roles are seeded by a migration and a user's role is protected by a foreign key, so a role that
/// is not there means the database is broken, not that the caller did something wrong. That is an exception (logged, and answered
/// with a generic 500), not a <c>Result</c> error.
/// </summary>
internal static class RoleRepositoryExtensions
{
    public static async Task<Role> GetRequiredByNameAsync(this IRoleRepository roles, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return await roles.GetByNameAsync(name, cancellationToken)
            ?? throw new InvalidOperationException($"The seeded role '{name}' is missing. Has the database been migrated?");
    }

    public static async Task<Role> GetRequiredByIdAsync(this IRoleRepository roles, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return await roles.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"The role {id} that a user holds does not exist. The foreign key should make that impossible.");
    }
}
