using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// A read-side view over the products and their stock: the one listing that has to look at both tables at once. Application owns the
/// interface and Infrastructure implements it. It only reads, and <b>every method names the store</b>, like the repositories of the tables
/// it reads (an architecture test says both).
/// </summary>
/// <remarks>
/// Why it is not <see cref="IProductRepository"/> or <see cref="IInventoryItemRepository"/>: "the live products whose stock is at or below its
/// level" cannot be paged from either table alone. Asking the stock table for its low rows and then the product table for their names would
/// page over rows that include archived products, and a page would come back short while the total still counted them.
/// </remarks>
public interface ILowStockRepository
{
    /// <summary>
    /// One page of the products on sale in that store whose stock is <b>at or below</b> their low-stock level, ordered by name.
    /// <paramref name="page"/> counts from 1; a page past the end is empty. A product with no level is never in it, an archived product is
    /// never in it, and another store's products are never in it, whatever the page.
    /// </summary>
    Task<PagedResult<InventoryEntryResult>> ListLowAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken);
}
