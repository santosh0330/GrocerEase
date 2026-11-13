namespace GrocerEase.Application.Inventory.Common;

/// <summary>The stock of a product after a change, as the use cases hand it back. Application's own type: it must not reference Contracts.</summary>
public sealed record StockResult(Guid ProductId, int QuantityOnHand);
