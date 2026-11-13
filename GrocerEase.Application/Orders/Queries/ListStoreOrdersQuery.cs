namespace GrocerEase.Application.Orders.Queries;

/// <summary>
/// Intent to read the orders of a store, one page at a time, optionally only those in one status (<paramref name="Status"/> is the name of an
/// <see cref="GrocerEase.Domain.Entities.OrderStatus"/>, in any case). <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.
/// </summary>
public sealed record ListStoreOrdersQuery(Guid StoreId, Guid ActorId, string? Status, int Page, int PageSize);
