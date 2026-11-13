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

    public Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken)
    {
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
