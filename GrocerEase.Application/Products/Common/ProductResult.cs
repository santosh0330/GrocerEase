namespace GrocerEase.Application.Products.Common;

/// <summary>A product as the use cases hand it back. Application's own type: it must not reference Contracts.</summary>
public sealed record ProductResult(Guid Id, Guid StoreId, string Name, string Description, decimal Price, int Version);
