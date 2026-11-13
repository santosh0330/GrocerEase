namespace GrocerEase.Application.Inventory.Queries;

/// <summary>
/// Intent to read the history of a product's stock, one page at a time, newest first. <paramref name="ActorId"/> is the signed-in user: the
/// use case checks that they run the store.
/// </summary>
public sealed record ListStockMovementsQuery(Guid StoreId, Guid ProductId, Guid ActorId, int Page, int PageSize);
