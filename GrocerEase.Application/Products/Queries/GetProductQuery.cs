namespace GrocerEase.Application.Products.Queries;

/// <summary>Intent to read one product that is on sale in a store.</summary>
public sealed record GetProductQuery(Guid StoreId, Guid ProductId);
