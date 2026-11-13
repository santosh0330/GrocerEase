using GrocerEase.Application.Abstractions;
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

    public Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken)
    {
        Calls.Add((productId, delta));
        Quantities.TryGetValue((storeId, productId), out var current);
        var next = (long)current + delta;
        if (next < 0 || next > InventoryItem.MaxQuantity)
        {
            return Task.FromResult<int?>(null);
        }

        Quantities[(storeId, productId)] = (int)next;
        return Task.FromResult<int?>((int)next);
    }
}
