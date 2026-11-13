using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Stores products in PostgreSQL through EF Core. Scoped, like the context it wraps. Every query names the store, so a product of
/// another store is simply not found.
/// </summary>
public sealed class ProductRepository(AppDbContext db) : IProductRepository
{
    // No tracking: a product is never changed through the context, so EF has no reason to remember it.
    public Task<Product?> GetLiveAsync(Guid storeId, Guid productId, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking()
            .SingleOrDefaultAsync(product => product.Id == productId && product.StoreId == storeId && product.ArchivedAt == null, cancellationToken);

    public async Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            // The partial unique index decided. Forget the entity so the context is usable again.
            db.Entry(product).State = EntityState.Detached;
            return false;
        }
    }

    // Only the name index counts. A missing store (the foreign key) or any other failure is a bug or an outage, and stays an exception.
    internal static bool IsDuplicateName(Exception exception) =>
        (exception as PostgresException ?? exception.InnerException as PostgresException) is
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProductConfiguration.LiveNameIndexName,
        };
}
