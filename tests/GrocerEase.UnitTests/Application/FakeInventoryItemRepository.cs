using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// A dictionary standing in for the database in handler tests. It follows the port's contract: no row means none, adding makes the row,
/// and a change that would leave the range is refused with <c>null</c> and changes nothing.
/// </summary>
internal sealed class FakeInventoryItemRepository : IInventoryItemRepository
{
    public Dictionary<(Guid StoreId, Guid ProductId), int> Quantities { get; } = [];

    /// <summary>Every change asked for, in the order it was asked: a test can say in which order products were reserved.</summary>
    public List<(Guid ProductId, int Delta)> Calls { get; } = [];

    /// <summary>The low-stock level of a product, where one has been set. A product has a level only if it has a row.</summary>
    public Dictionary<(Guid StoreId, Guid ProductId), int> Thresholds { get; } = [];

    public Task<IReadOnlyDictionary<Guid, InventoryItem>> GetItemsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, InventoryItem>>(
            Quantities.Where(entry => entry.Key.StoreId == storeId && productIds.Contains(entry.Key.ProductId))
                .ToDictionary(
                    entry => entry.Key.ProductId,
                    entry => new InventoryItem(entry.Key.ProductId, storeId, entry.Value, Thresholds.TryGetValue(entry.Key, out var level) ? level : null)));

    public Task<int?> SetLowStockThresholdAsync(Guid storeId, Guid productId, int? threshold, CancellationToken cancellationToken)
    {
        // Like the real statement: a product with no row gets one at zero, and only the level is written, never the quantity.
        Quantities.TryAdd((storeId, productId), 0);
        if (threshold is { } level)
        {
            Thresholds[(storeId, productId)] = level;
        }
        else
        {
            Thresholds.Remove((storeId, productId));
        }

        return Task.FromResult<int?>(Quantities[(storeId, productId)]);
    }

    public Task<int?> TryAdjustAsync(Guid storeId, Guid productId, StockChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var delta = change.Delta;
        Calls.Add((productId, delta));
        Quantities.TryGetValue((storeId, productId), out var current);
        var next = (long)current + delta;
        if (next < 0 || next > InventoryItem.MaxQuantity)
        {
            return Task.FromResult<int?>(null);
        }

        Quantities[(storeId, productId)] = (int)next;

        // Like the real statement: a change that is made leaves its movement, and a refused one leaves none. The entity refuses a movement
        // that makes no sense, as the repository does before it runs any SQL.
        Movements.Add(new StockMovement(Guid.CreateVersion7(), productId, storeId, delta, change.Reason, change.ActorId, change.OrderId, DateTimeOffset.UtcNow));
        Changes.Add(change);
        return Task.FromResult<int?>((int)next);
    }

    /// <summary>The ledger: one movement for every change that was made, in the order they were made. A refused change leaves nothing.</summary>
    public List<StockMovement> Movements { get; } = [];

    /// <summary>Every change that was made (not the refused ones), with its reason, actor and order.</summary>
    public List<StockChange> Changes { get; } = [];

    public Task<PagedResult<StockMovement>> ListMovementsAsync(Guid storeId, Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var history = Movements.Where(movement => movement.StoreId == storeId && movement.ProductId == productId)
            .OrderByDescending(movement => movement.CreatedAt).ThenByDescending(movement => movement.Id).ToList();
        return Task.FromResult(new PagedResult<StockMovement>([.. history.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, history.Count));
    }
}
