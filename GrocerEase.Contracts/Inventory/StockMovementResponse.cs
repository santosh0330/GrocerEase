namespace GrocerEase.Contracts.Inventory;

/// <summary>
/// One line of a product's stock history, as <c>GET /stores/{storeId}/products/{productId}/stock-movements</c> returns it: by how much the
/// quantity changed (negative when stock was removed), why (<c>OpeningBalance</c>, <c>Received</c>, <c>Correction</c>, <c>OrderPlaced</c> or
/// <c>OrderCancelled</c>), who did it (<c>null</c> only for the opening balance), which order it was for (<c>null</c> unless an order did it),
/// and when.
/// </summary>
public sealed record StockMovementResponse(Guid Id, Guid ProductId, int Delta, string Reason, Guid? ActorId, Guid? OrderId, DateTimeOffset CreatedAt);
