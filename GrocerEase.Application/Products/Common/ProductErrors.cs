using GrocerEase.Application.Common;

namespace GrocerEase.Application.Products.Common;

/// <summary>The expected failures of the product use cases. Codes are stable: clients may switch on them.</summary>
public static class ProductErrors
{
    public static readonly ResultError DuplicateName = new(
        "Product.DuplicateName", "This store already sells a product with that name.", ErrorType.Conflict);

    /// <summary>
    /// The edit was made from a version of the product that is no longer the current one: someone changed it after the client read it.
    /// Nothing was written. The error carries the product as it is now, so the client can show the difference and try again from it.
    /// </summary>
    public static ResultError Outdated(ProductResult current) => new(
        "Product.Outdated",
        "The product was changed by someone else after you read it. Nothing was saved: look at the current product and make your change on top of it.",
        ErrorType.Conflict,
        current);

    // One error for a product that never existed, one that was archived, and one that belongs to another store.
    public static readonly ResultError NotFound = new(
        "Product.NotFound", "There is no such product in this store.", ErrorType.NotFound);
}
