using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>Stores stores in PostgreSQL through EF Core. Scoped, like the context it wraps: one instance per request.</summary>
public sealed class StoreRepository(AppDbContext db) : IStoreRepository
{
    // No tracking on reads: a store is never changed through the context, so EF has no reason to remember it.
    public Task<Store?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Stores.AsNoTracking().SingleOrDefaultAsync(store => store.Id == id, cancellationToken);

    public async Task<PagedResult<Store>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        // The name is unique (ix_stores_name), so it orders the pages by itself. The count and the page are two queries; a store created
        // between them makes the total one more than the page shows, which a client refreshing the list absorbs.
        var total = await db.Stores.CountAsync(cancellationToken);
        var items = await db.Stores.AsNoTracking().OrderBy(store => store.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Store>(items, page, pageSize, total);
    }

    public async Task<bool> TryAddAsync(Store store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        db.Stores.Add(store);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            // The unique index decided. Forget the entity so the context is usable again.
            db.Entry(store).State = EntityState.Detached;
            return false;
        }
    }

    // Only the name index counts. A clash on the primary key or any other failure is a bug or an outage, and stays an exception.
    private static bool IsDuplicateName(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StoreConfiguration.NameIndexName,
        };
}
