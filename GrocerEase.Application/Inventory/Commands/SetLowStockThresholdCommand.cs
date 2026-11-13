namespace GrocerEase.Application.Inventory.Commands;

/// <summary>
/// Intent to set the low-stock level of a product (<c>null</c> clears it: the product is then never low). <paramref name="ActorId"/> is the
/// signed-in user: the use case checks that they run the store.
/// </summary>
public sealed record SetLowStockThresholdCommand(Guid StoreId, Guid ProductId, Guid ActorId, int? Threshold);
