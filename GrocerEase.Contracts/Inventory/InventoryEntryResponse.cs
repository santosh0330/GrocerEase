namespace GrocerEase.Contracts.Inventory;

/// <summary>
/// One line of a store's inventory, as <c>GET /stores/{storeId}/inventory</c> returns it: the stock on hand, the level at which the store wants
/// to be told the product is running out (<c>null</c> when none is set), and whether the stock is at or below it.
/// </summary>
public sealed record InventoryEntryResponse(Guid ProductId, string ProductName, int QuantityOnHand, int? LowStockThreshold, bool IsLow);
