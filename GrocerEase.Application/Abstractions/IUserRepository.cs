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

    /// <summary>
    /// Stores a new user. Returns <c>false</c>, and stores nothing, when the email is already taken. The caller checks first,
    /// but two registrations can race between the check and the add, so the store has the last word: this is an expected outcome,
    /// not an exception. Any other failure (the store being unreachable, say) is an exception.
    /// </summary>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);
}
