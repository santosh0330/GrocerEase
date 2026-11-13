namespace GrocerEase.Contracts.Inventory;

/// <summary>One line of a store's inventory, as <c>GET /stores/{storeId}/inventory</c> returns it.</summary>
public sealed record InventoryEntryResponse(Guid ProductId, string ProductName, int QuantityOnHand);
