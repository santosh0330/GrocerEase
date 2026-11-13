namespace GrocerEase.Contracts.Products;

/// <summary>One product, as <c>/stores/{storeId}/products</c> returns it.</summary>
public sealed record ProductResponse(Guid Id, Guid StoreId, string Name, string Description, decimal Price);
