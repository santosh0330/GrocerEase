namespace GrocerEase.Contracts.Products;

/// <summary>Body of <c>POST /stores/{storeId}/products</c>. The description is optional.</summary>
public sealed record CreateProductRequest(string Name, string? Description, decimal Price);
