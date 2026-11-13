namespace GrocerEase.Application.Orders.Queries;

/// <summary>
/// Intent to read the orders of every store, one page at a time, optionally only those of one store (<paramref name="StoreId"/>) and only those
/// in one status (<paramref name="Status"/> is the name of an <see cref="GrocerEase.Domain.Entities.OrderStatus"/>, in any case). It names no
/// caller: the permission to read every store's orders is what lets a request reach it.
/// </summary>
public sealed record ListAllOrdersQuery(Guid? StoreId, string? Status, int Page, int PageSize);
