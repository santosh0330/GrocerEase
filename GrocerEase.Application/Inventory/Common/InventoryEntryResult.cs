namespace GrocerEase.Application.Inventory.Common;

/// <summary>One line of a store's inventory: a product on sale and how many units are on hand (zero when it has no stock row).</summary>
public sealed record InventoryEntryResult(Guid ProductId, string ProductName, int QuantityOnHand);
