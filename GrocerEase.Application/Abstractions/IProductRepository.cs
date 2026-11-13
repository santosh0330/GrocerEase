using GrocerEase.Application.Common;
using GrocerEase.Application.Products.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where products are kept. Application owns the interface and Infrastructure implements it. <b>Every method names the store</b>
/// (or takes a product that carries it): a product is always asked for in the store it belongs to, so a query that forgets to scope by
/// store cannot be written. An architecture test says so.
/// </summary>
public interface IProductRepository
{
    /// <summary>Finds a product that is on sale in that store. Returns <c>null</c> when there is none, or it is archived, or it is another store's.</summary>
    Task<Product?> GetLiveAsync(Guid storeId, Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the products that are on sale in that store, ordered by name and then by id so that the order is the same on every
    /// request. <paramref name="page"/> counts from 1. A page past the end is empty.
    /// </summary>
    Task<PagedResult<Product>> ListLiveAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the name, description and price of <paramref name="product"/> over the one that is on sale in its store with its id. One
    /// statement with the store and the "still on sale" condition inside it, so a product of another store is never touched and an
    /// archived one is not brought back. The id and the store are not changed.
    /// </summary>
    Task<ProductUpdateOutcome> TryUpdateAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a product off sale by recording when. Returns <c>false</c>, and changes nothing, when it is not on sale in that store: it
    /// never existed, is another store's, or was archived already. One statement, so two requests archiving the same product cannot both
    /// succeed. The row stays: an order that bought it can still say what it was.
    /// </summary>
    Task<bool> TryArchiveAsync(Guid storeId, Guid productId, DateTimeOffset archivedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a new product. Returns <c>false</c>, and stores nothing, when the store already sells a product with that name. The
    /// unique index has the last word, as for a user's email: this is an expected outcome, not an exception.
    /// </summary>
    Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken);
}
