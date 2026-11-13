namespace GrocerEase.Application.Products.Queries;

/// <summary>Intent to browse the products that are on sale in a store, one page at a time.</summary>
public sealed record ListProductsQuery(Guid StoreId, int Page, int PageSize);
