using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A dictionary standing in for the database in handler tests. It follows the port's contract: lookups ignore case, a duplicate email is refused.</summary>
internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<string, User> _usersByEmail = new(StringComparer.Ordinal);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        _usersByEmail.TryGetValue(User.NormalizeEmail(email), out var user);
        return Task.FromResult(user);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_usersByEmail.Values.SingleOrDefault(user => user.Id == id));

    public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Task.FromResult(_usersByEmail.TryAdd(user.Email, user));
    }
}
