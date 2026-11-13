namespace GrocerEase.Application.Inventory.Queries;

/// <summary>Intent to read the stock of a store, one page at a time. <paramref name="ActorId"/> is the signed-in user: the use case checks that they run the store.</summary>
public sealed record ListInventoryQuery(Guid StoreId, Guid ActorId, int Page, int PageSize);
