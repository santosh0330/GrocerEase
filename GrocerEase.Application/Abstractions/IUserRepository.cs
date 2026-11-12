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

    /// <summary>Finds a user by id. Returns <c>null</c> when there is none.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new user. Returns <c>false</c>, and stores nothing, when the email is already taken. The caller checks first,
    /// but two registrations can race between the check and the add, so the store has the last word: this is an expected outcome,
    /// not an exception. Any other failure (the store being unreachable, say) is an exception.
    /// </summary>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the role a user holds from <paramref name="fromRoleId"/> to <paramref name="toRoleId"/>, and only if the user holds
    /// <paramref name="fromRoleId"/> at that moment. Returns <c>false</c>, and changes nothing, when there is no such user or the user
    /// holds another role. It is one statement with the condition inside it, not a read followed by a write, so two requests that
    /// both saw a customer cannot both change them: the second finds the role already changed. A caller that reads the user first
    /// is only choosing a good error message; this is what keeps the data right. The role is not an entity here: nothing in the
    /// domain reads it, and the change must not be overtaken.
    /// </summary>
    Task<bool> TryChangeRoleAsync(Guid userId, Guid fromRoleId, Guid toRoleId, CancellationToken cancellationToken);
}
