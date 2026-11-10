using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Stores users in PostgreSQL through EF Core. Scoped, like the context it wraps: one instance per request.
/// </summary>
public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // Emails are stored in one normalised form, so the lookup uses the same form and a plain equality hits the unique index.
        var normalized = User.NormalizeEmail(email);

        // No tracking: a user is never changed through the context, so EF has no reason to remember it.
        return db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == normalized, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        // The primary key lookup. No tracking, for the same reason as above.
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public async Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateEmail(exception))
        {
            // The unique index decided, and it is the only thing that can: a check-then-insert would race. Forget the entity so
            // the context is usable again (it would otherwise try to insert it a second time on the next save).
            db.Entry(user).State = EntityState.Detached;
            return false;
        }
    }

    // Only the email index counts. A clash on the primary key (the same id twice) or any other failure is a bug or an outage
    // and must stay an exception, not be reported to the caller as "that email is taken".
    private static bool IsDuplicateEmail(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: UserConfiguration.EmailIndexName,
        };
}
