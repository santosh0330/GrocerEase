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
    /// Stores a new product. Returns <c>false</c>, and stores nothing, when the store already sells a product with that name. The
    /// unique index has the last word, as for a user's email: this is an expected outcome, not an exception.
    /// </summary>
    Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken);
}
