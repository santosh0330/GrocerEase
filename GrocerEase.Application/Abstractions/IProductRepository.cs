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
    /// The products among <paramref name="productIds"/> that are on sale in that store. One that is archived, unknown or another store's
    /// is simply not in the result, so a caller that asked for three and got two knows one is not available.
    /// </summary>
    Task<IReadOnlyList<Product>> GetLiveByIdsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the products that are on sale in that store, ordered by name and then by id so that the order is the same on every
    /// request. <paramref name="page"/> counts from 1. A page past the end is empty.
    /// </summary>
    Task<PagedResult<Product>> ListLiveAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the name, description and price of <paramref name="product"/> over the one that is on sale in its store with its id, <b>if it
    /// is still at <paramref name="expectedVersion"/></b>, and makes the row the next version. One statement with the store, the "still on
    /// sale" condition and the version inside it, so a product of another store is never touched, an archived one is not brought back, and
    /// of two edits made from the same version one is written and the other is <see cref="ProductUpdateOutcome.Outdated"/>. The id and the
    /// store are not changed.
    /// </summary>
    /// <remarks>
    /// This is the optimistic check: nothing is locked while a person edits, and a stale edit is found out when it is written, by the
    /// <c>WHERE</c> that matches no row. There is no exception to catch: the answer is a value.
    /// </remarks>
    Task<ProductUpdateOutcome> TryUpdateAsync(Product product, int expectedVersion, CancellationToken cancellationToken);

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
