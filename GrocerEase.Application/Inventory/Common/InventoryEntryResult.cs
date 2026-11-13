using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Common;

/// <summary>
/// One line of a store's inventory: a product on sale, how many units are on hand (zero when it has no stock row), and the level at which
/// the store wants to be told it is running out (<c>null</c> when it has set none, and then it is never low).
/// </summary>
public sealed record InventoryEntryResult(Guid ProductId, string ProductName, int QuantityOnHand, int? LowStockThreshold = null)
{
    /// <summary>At or below the level. <see cref="InventoryItem.IsLowAt"/> is the one definition.</summary>
    public bool IsLow => InventoryItem.IsLowAt(QuantityOnHand, LowStockThreshold);
}
