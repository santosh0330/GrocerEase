namespace GrocerEase.Contracts.Products;

/// <summary>
/// Body of <c>PUT /stores/{storeId}/products/{productId}</c>: the product's new details. The description is optional.
/// <paramref name="Version"/> is <b>required</b>: the <c>version</c> of the product as you read it. If the product has been changed since,
/// nothing is saved and the answer is <c>409 Product.Outdated</c> with the product as it is now.
/// </summary>
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price, int? Version);
