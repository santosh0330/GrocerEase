using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// The seeded roles, with what each grants, in memory, standing in for the database in handler tests. <c>seeded: false</c> is a database that was never
/// migrated: no role exists at all.
/// </summary>
internal sealed class FakeRoleRepository(bool seeded = true) : IRoleRepository
{
    private readonly List<Role> _roles = seeded ? [Customer(), Admin(), StoreUser()] : [];

    /// <summary>What the database is seeded with, written out here so a test says what it expects instead of reading the seed.</summary>
    public static Role Customer()
    {
        var role = new Role(RoleIds.Customer, RoleNames.Customer);
        role.Grant(Permissions.ProfileRead);
        role.Grant(Permissions.StoresRead);
        role.Grant(Permissions.ProductsRead);
        return role;
    }

    public static Role Admin()
    {
        var role = new Role(RoleIds.Admin, RoleNames.Admin);
        role.Grant(Permissions.ProfileRead);
        role.Grant(Permissions.RolesRead);
        role.Grant(Permissions.StoresManage);
        role.Grant(Permissions.StoresRead);
        role.Grant(Permissions.ProductsRead);
        return role;
    }

    public static Role StoreUser()
    {
        var role = new Role(RoleIds.StoreUser, RoleNames.StoreUser);
        role.Grant(Permissions.ProfileRead);
        role.Grant(Permissions.StoresRead);
        role.Grant(Permissions.ProductsRead);
        role.Grant(Permissions.ProductsManage);
        return role;
    }

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.SingleOrDefault(role => role.Id == id));

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Task.FromResult(_roles.SingleOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal)));
    }

    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. _roles.OrderBy(role => role.Name, StringComparer.Ordinal)]);
}
