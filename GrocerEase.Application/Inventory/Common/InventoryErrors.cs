using GrocerEase.Application.Common;

namespace GrocerEase.Application.Inventory.Common;

/// <summary>The expected failures of the inventory use cases. Codes are stable: clients may switch on them.</summary>
public static class InventoryErrors
{
    public static readonly ResultError NotEnoughStock = new(
        "Inventory.NotEnoughStock", "There is not enough stock to remove that many: the quantity would go below zero.", ErrorType.Conflict);

    public static readonly ResultError TooMuchStock = new(
        "Inventory.TooMuchStock", "That would put more stock on hand than a product may hold.", ErrorType.Conflict);
}
