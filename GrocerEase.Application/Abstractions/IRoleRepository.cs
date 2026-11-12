using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where roles are read from. Application owns the interface and Infrastructure implements it, like
/// <see cref="IUserRepository"/>. There is no way to add or change a role here: the roles are seeded, and nothing in the
/// application edits them yet.
/// </summary>
public interface IRoleRepository
{
    /// <summary>Finds a role by id. Returns <c>null</c> when there is none.</summary>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Finds a role by its exact name (<see cref="RoleNames"/>). Returns <c>null</c> when there is none.</summary>
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken);

    /// <summary>Every role, ordered by name.</summary>
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);
}
