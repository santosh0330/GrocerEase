using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Inventory.Common;

/// <summary>
/// One line of a store's inventory: a product on sale, how many units are on hand (zero when it has no stock row), the level at which the
/// store wants to be told it is running out (<c>null</c> when it has set none, and then it is never low), and whether it is low now.
/// </summary>
/// <remarks>
/// <paramref name="IsLow"/> is not worked out here: the use cases make an entry from the stock row (<see cref="FromItem"/>), so
/// <see cref="InventoryItem.IsLowExpression"/> stays the only definition of "low".
/// </remarks>
public sealed record InventoryEntryResult(Guid ProductId, string ProductName, int QuantityOnHand, int? LowStockThreshold = null, bool IsLow = false)
{
    /// <summary>An entry for a product and its stock row; whether it is low is the entity's answer.</summary>
    public static InventoryEntryResult FromItem(string productName, InventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryEntryResult(item.ProductId, productName, item.QuantityOnHand, item.LowStockThreshold, item.IsLow);
    }

    /// <summary>An entry for a product that has no stock row: none on hand, no level, and so never low.</summary>
    public static InventoryEntryResult WithNoStock(Guid productId, string productName) => new(productId, productName, 0);
}
