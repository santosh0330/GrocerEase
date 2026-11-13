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
        // Through the entity, like the real query goes through the same expression: the fake has no second definition of "low".
        var low = products.Products
            .Where(product => product.StoreId == storeId && product.ArchivedAt is null)
            .Select(product => (product, item: stock.Quantities.TryGetValue((storeId, product.Id), out var quantity)
                ? new InventoryItem(product.Id, storeId, quantity, stock.Thresholds.TryGetValue((storeId, product.Id), out var level) ? level : null)
                : null))
            .Where(pair => pair.item is { IsLow: true })
            .OrderBy(pair => pair.product.Name, StringComparer.Ordinal).ThenBy(pair => pair.product.Id)
            .Select(pair => InventoryEntryResult.FromItem(pair.product.Name, pair.item!))
            .ToList();
        return Task.FromResult(new PagedResult<InventoryEntryResult>([.. low.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, low.Count));
    }
}
