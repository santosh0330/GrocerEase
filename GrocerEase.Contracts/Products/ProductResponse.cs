namespace GrocerEase.Contracts.Products;

/// <summary>
/// One product, as <c>/stores/{storeId}/products</c> returns it. <paramref name="Version"/> is 1 when the product is made and one more for
/// every change: send it back when you edit, and the edit is refused if someone changed the product after you read it.
/// </summary>
public sealed record ProductResponse(Guid Id, Guid StoreId, string Name, string Description, decimal Price, int Version);
