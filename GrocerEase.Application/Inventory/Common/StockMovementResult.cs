namespace GrocerEase.Application.Inventory.Common;

/// <summary>One line of a product's stock history, as the use cases hand it back. Application's own type: it must not reference Contracts.</summary>
public sealed record StockMovementResult(Guid Id, Guid ProductId, int Delta, string Reason, Guid? ActorId, Guid? OrderId, DateTimeOffset CreatedAt);
