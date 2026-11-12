using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// The seeded roles in memory, standing in for the database in handler tests. <c>seeded: false</c> is a database that was never
/// migrated: no role exists at all.
/// </summary>
internal sealed class FakeRoleRepository(bool seeded = true) : IRoleRepository
{
    private readonly List<Role> _roles = seeded
        ? [new Role(RoleIds.Customer, RoleNames.Customer), new Role(RoleIds.Admin, RoleNames.Admin)]
        : [];

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
