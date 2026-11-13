namespace GrocerEase.Application.Inventory.Commands;

/// <summary>
/// Intent to change the stock of a product by <paramref name="Delta"/> (negative to remove). <paramref name="ActorId"/> is the signed-in
/// user: the use case checks that they run the store.
/// </summary>
public sealed record AdjustStockCommand(Guid StoreId, Guid ProductId, Guid ActorId, int Delta);
