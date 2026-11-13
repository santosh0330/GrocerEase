namespace GrocerEase.Application.Orders.Queries;

/// <summary>Intent to read the signed-in customer's own orders, newest first, one page at a time.</summary>
public sealed record ListMyOrdersQuery(Guid CustomerId, int Page, int PageSize);
