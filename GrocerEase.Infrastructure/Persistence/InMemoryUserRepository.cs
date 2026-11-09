using System.Collections.Concurrent;
using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// TEMPORARY: keeps users in memory, so they vanish when the process stops and are not shared between instances.
/// Register it as a singleton, or every request would see an empty store. Replace with a PostgreSQL repository.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    // The email is the key, compared without case: "Ada@x.com" and "ada@x.com" are the same account.
    private readonly ConcurrentDictionary<string, User> _usersByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        _usersByEmail.TryGetValue(email, out var user);
        return Task.FromResult(user);
    }

    public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // TryAdd is atomic, so of two simultaneous registrations of one email exactly one gets true.
        return Task.FromResult(_usersByEmail.TryAdd(user.Email, user));
    }
}
