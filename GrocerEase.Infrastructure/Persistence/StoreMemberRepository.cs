using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>Stores the memberships of stores in PostgreSQL through EF Core. Scoped, like the context it wraps: one instance per request.</summary>
public sealed class StoreMemberRepository(AppDbContext db) : IStoreMemberRepository
{
    public async Task<bool> TryAddAsync(StoreMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);

        db.StoreMembers.Add(member);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUserAlreadyInAStore(exception))
        {
            // The unique index decided. Forget the entity so the context is usable again.
            db.Entry(member).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<bool> TryRemoveAsync(Guid storeId, Guid userId, CancellationToken cancellationToken) =>
        // DELETE ... WHERE store_id = @store AND user_id = @user: both in the statement, so another store's membership of the user is not touched
        // and a second request for the same member finds nothing to delete.
        await db.StoreMembers.Where(member => member.StoreId == storeId && member.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 1;

    // Only the user index counts. A missing store or user (the foreign keys), or any other failure, is a bug or an outage and stays an exception.
    private static bool IsUserAlreadyInAStore(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StoreMemberConfiguration.UserIndexName,
        };
}
