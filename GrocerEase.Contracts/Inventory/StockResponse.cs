namespace GrocerEase.Contracts.Inventory;

/// <summary>The stock of a product after a change.</summary>
public sealed record StockResponse(Guid ProductId, int QuantityOnHand);
