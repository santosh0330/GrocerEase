namespace GrocerEase.Contracts.Products;

/// <summary>Body of <c>PUT /stores/{storeId}/products/{productId}</c>: the product's new details. The description is optional.</summary>
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price);
