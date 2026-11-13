namespace GrocerEase.Application.Stores.Queries;

/// <summary>Intent to read the stores, one page at a time. <paramref name="Page"/> counts from 1.</summary>
public sealed record ListStoresQuery(int Page, int PageSize);
