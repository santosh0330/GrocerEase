using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Common;
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

    public async Task<IReadOnlyList<Product>> GetLiveByIdsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        // The store is in the query as well as the ids, so another store's product asked for by its id is simply not found.
        return await db.Products.AsNoTracking()
            .Where(product => product.StoreId == storeId && product.ArchivedAt == null && productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Product>> ListLiveAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var live = db.Products.AsNoTracking().Where(product => product.StoreId == storeId && product.ArchivedAt == null);

        // The count and the page are two queries; a product added between them makes the total one more than the page shows, which a client
        // refreshing the list absorbs. Live names are unique within a store (the partial index), so the name already orders the page; the id
        // is a cheap guarantee that the order stays total, and so the pages stay stable, if that index ever changes.
        var total = await live.CountAsync(cancellationToken);
        var items = await live.OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Product>(items, page, pageSize, total);
    }

    public async Task<ProductUpdateOutcome> TryUpdateAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            // UPDATE products SET name, description, price WHERE id = @id AND store_id = @store AND archived_at IS NULL
            var changed = await db.Products
                .Where(existing => existing.Id == product.Id && existing.StoreId == product.StoreId && existing.ArchivedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(existing => existing.Name, product.Name)
                        .SetProperty(existing => existing.Description, product.Description)
                        .SetProperty(existing => existing.Price, product.Price),
                    cancellationToken);
            return changed == 1 ? ProductUpdateOutcome.Updated : ProductUpdateOutcome.NotFound;
        }
        catch (PostgresException exception) when (IsDuplicateName(exception))
        {
            // ExecuteUpdate does not wrap the database's error the way SaveChanges does, so it is the PostgresException itself.
            return ProductUpdateOutcome.DuplicateName;
        }
    }

    public async Task<bool> TryArchiveAsync(Guid storeId, Guid productId, DateTimeOffset archivedAt, CancellationToken cancellationToken) =>
        // UPDATE products SET archived_at = @now WHERE id = @id AND store_id = @store AND archived_at IS NULL
        await db.Products
            .Where(product => product.Id == productId && product.StoreId == storeId && product.ArchivedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.ArchivedAt, archivedAt), cancellationToken) == 1;

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
