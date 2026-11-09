using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where users are stored. Application owns the interface and Infrastructure implements it,
/// so a handler never knows whether the users live in memory or in PostgreSQL.
/// </summary>
public interface IUserRepository
{
    /// <summary>Finds a user by email, ignoring case. Returns <c>null</c> when there is none.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>Stores a new user. The caller checks for a duplicate email first; a duplicate here is a bug and throws.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);
}
