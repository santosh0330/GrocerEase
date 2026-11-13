using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Answers "which products are running out" with one query over the products and their stock rows. It reads and never writes (an
/// architecture test says so), and every query names the store, on both tables.
/// </summary>
public sealed class LowStockRepository(AppDbContext db) : ILowStockRepository
{
    public async Task<PagedResult<InventoryEntryResult>> ListLowAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        // At or below the level, as InventoryItem.IsLowAt says. A row with no level (null) is never low: in SQL, "x <= null" is not true, and the
        // explicit test says so. The join is on the product, and both sides are in the store.
        var low =
            from item in db.InventoryItems.AsNoTracking()
            join product in db.Products.AsNoTracking() on item.ProductId equals product.Id
            where item.StoreId == storeId
                && product.StoreId == storeId
                && product.ArchivedAt == null
                && item.LowStockThreshold != null
                && item.QuantityOnHand <= item.LowStockThreshold
            select new { product.Id, product.Name, item.QuantityOnHand, item.LowStockThreshold };

        // Two queries, as for every list: a product that becomes low between them makes the total one more than the page shows.
        var total = await low.CountAsync(cancellationToken);
        var rows = await low.OrderBy(row => row.Name).ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<InventoryEntryResult>(
            [.. rows.Select(row => new InventoryEntryResult(row.Id, row.Name, row.QuantityOnHand, row.LowStockThreshold))], page, pageSize, total);
    }
}
