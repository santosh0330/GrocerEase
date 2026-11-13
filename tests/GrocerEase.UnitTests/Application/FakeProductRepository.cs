using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: every question names the store, and a live name is unique within it.</summary>
internal sealed class FakeProductRepository : IProductRepository
{
    public List<Product> Products { get; } = [];

    public Task<Product?> GetLiveAsync(Guid storeId, Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(product => product.Id == productId && product.StoreId == storeId && !product.IsArchived));

    public Task<PagedResult<Product>> ListLiveAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var live = Products.Where(product => product.StoreId == storeId && !product.IsArchived).ToList();
        var items = live.OrderBy(product => product.Name, StringComparer.Ordinal).ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<Product>(items, page, pageSize, live.Count));
    }

    public Task<bool> TryAddAsync(Product product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (Products.Exists(existing => existing.StoreId == product.StoreId && !existing.IsArchived && string.Equals(existing.Name, product.Name, StringComparison.Ordinal)))
        {
            return Task.FromResult(false);
        }

        Products.Add(product);
        return Task.FromResult(true);
    }
}
