namespace GrocerEase.Application.Inventory.Commands;

/// <summary>
/// Intent to change the stock of a product by <paramref name="Delta"/> (negative to remove). <paramref name="ActorId"/> is the signed-in
/// user: the use case checks that they run the store. <paramref name="Reason"/> is the name of a
/// <see cref="GrocerEase.Domain.Entities.StockMovementReason"/> the person may choose (<c>Received</c> or <c>Correction</c>, in any case), or
/// <c>null</c> to let the sign of the change decide.
/// </summary>
public sealed record AdjustStockCommand(Guid StoreId, Guid ProductId, Guid ActorId, int Delta, string? Reason = null);
