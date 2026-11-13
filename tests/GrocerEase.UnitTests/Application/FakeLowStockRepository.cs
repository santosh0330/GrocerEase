using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// Stands in for the join in handler tests, over the same fakes the other repositories use. It follows the port's contract: the live
/// products of that store whose stock is at or below its level, by name; no level, no row, an archived product or another store's: never.
/// </summary>
internal sealed class FakeLowStockRepository(FakeProductRepository products, FakeInventoryItemRepository stock) : ILowStockRepository
{
    public Task<PagedResult<InventoryEntryResult>> ListLowAsync(Guid storeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var low = products.Products
            .Where(product => product.StoreId == storeId && product.ArchivedAt is null)
            .Where(product => stock.Thresholds.TryGetValue((storeId, product.Id), out var level)
                && stock.Quantities.TryGetValue((storeId, product.Id), out var quantity)
                && InventoryItem.IsLowAt(quantity, level))
            .OrderBy(product => product.Name, StringComparer.Ordinal).ThenBy(product => product.Id)
            .Select(product => new InventoryEntryResult(product.Id, product.Name, stock.Quantities[(storeId, product.Id)], stock.Thresholds[(storeId, product.Id)]))
            .ToList();
        return Task.FromResult(new PagedResult<InventoryEntryResult>([.. low.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, low.Count));
    }
}
