using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where the stock of products is kept. Application owns the interface and Infrastructure implements it. <b>Every method names the
/// store</b>, like <see cref="IProductRepository"/>: stock is always asked for in the store it belongs to (an architecture test says so).
/// </summary>
public interface IInventoryItemRepository
{
    /// <summary>
    /// Adds <paramref name="delta"/> (negative to remove) to the quantity on hand of a product of that store, and returns the quantity
    /// now. Returns <c>null</c>, and changes nothing, when the result would be below zero or above
    /// <see cref="GrocerEase.Domain.Entities.InventoryItem.MaxQuantity"/>. A product with no stock row has none: adding makes the row,
    /// removing is refused.
    /// </summary>
    /// <remarks>
    /// One SQL statement with the limits inside it, never a read followed by a write: two changes at the same moment both count, and the
    /// quantity can never be driven below zero by two requests that each saw enough. The caller checks that the product exists in the
    /// store; a product archived in the instant between is harmless, since the stock of an archived product is simply not shown.
    /// </remarks>
    Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the low-stock level of a product of that store (<c>null</c> clears it) and returns the quantity on hand. A product with no stock
    /// row gets one, at zero: the level is stored on the row, and "no stock" is a quantity of zero. Returns <c>null</c>, and changes nothing,
    /// when the product is not in that store.
    /// </summary>
    /// <remarks>
    /// One statement that writes <b>only the level</b>. The quantity is not in it, so a level set at the same moment as an adjustment or an
    /// order cannot overwrite the quantity with a stale number, and the adjustments cannot overwrite the level. The last level set wins, as for
    /// any setting.
    /// </remarks>
    Task<int?> SetLowStockThresholdAsync(Guid storeId, Guid productId, int? threshold, CancellationToken cancellationToken);

    /// <summary>
    /// The stock row (quantity and low-stock level) of each of these products in that store. A product with no stock row is not in the
    /// result: it has none, and no level. Other stores' rows are never returned, whatever ids are asked for.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, InventoryItem>> GetItemsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}
