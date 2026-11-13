using GrocerEase.Application.Common;

namespace GrocerEase.Application.Products.Common;

/// <summary>The expected failures of the product use cases. Codes are stable: clients may switch on them.</summary>
public static class ProductErrors
{
    public static readonly ResultError DuplicateName = new(
        "Product.DuplicateName", "This store already sells a product with that name.", ErrorType.Conflict);

    // One error for a product that never existed, one that was archived, and one that belongs to another store.
    public static readonly ResultError NotFound = new(
        "Product.NotFound", "There is no such product in this store.", ErrorType.NotFound);
}
