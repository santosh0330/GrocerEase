using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: every question names the store, and a live name is unique within it.</summary>
internal sealed class FakeProductRepository : IProductRepository
{
    public List<Product> Products { get; } = [];

    public Task<Product?> GetLiveAsync(Guid storeId, Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(Products.SingleOrDefault(product => product.Id == productId && product.StoreId == storeId && !product.IsArchived));

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
