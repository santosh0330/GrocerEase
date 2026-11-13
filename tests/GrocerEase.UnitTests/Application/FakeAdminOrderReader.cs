using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: every store's orders, newest first, optionally of one store and in one status.</summary>
internal sealed class FakeAdminOrderReader : IAdminOrderReader
{
    public List<Order> Orders { get; } = [];

    public Task<PagedResult<Order>> ListAsync(Guid? storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var matching = Orders.Where(order => (storeId is null || order.StoreId == storeId) && (status is null || order.Status == status))
            .OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id).ToList();
        return Task.FromResult(new PagedResult<Order>([.. matching.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, matching.Count));
    }
}
