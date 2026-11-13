using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Products.Common;

internal static class ProductResultMapping
{
    public static ProductResult ToResult(this Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new ProductResult(product.Id, product.StoreId, product.Name, product.Description, product.Price, product.Version);
    }
}
