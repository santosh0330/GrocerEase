using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Orders.Common;

/// <summary>What a cancelled order does to the shelf, shared by the store's cancellation and the customer's.</summary>
internal static class OrderStock
{
    /// <summary>
    /// Puts the quantity of every line of a cancelled order back, in order of product id (like the lines are reserved: a stock row stays
    /// locked until the unit ends, and two cancellations that share products and take them in opposite orders would deadlock). Returns
    /// <c>null</c> when all of it went back, or the error to answer with when a quantity would pass the most a store can hold; the caller's
    /// unit of work then rolls back the status change and every line already restored.
    /// </summary>
    public static async Task<ResultError?> RestoreAsync(
        IInventoryItemRepository inventoryRepository, Order order, CancellationToken cancellationToken)
    {
        foreach (var line in order.Lines.OrderBy(line => line.ProductId))
        {
            if (await inventoryRepository.TryAdjustAsync(order.StoreId, line.ProductId, line.Quantity, cancellationToken) is null)
            {
                return OrderErrors.CannotRestoreStock(line.ProductName);
            }
        }

        return null;
    }
}
