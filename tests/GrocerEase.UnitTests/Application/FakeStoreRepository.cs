using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: a duplicate name is refused, and a list is ordered by name.</summary>
internal sealed class FakeStoreRepository : IStoreRepository
{
    private readonly List<Store> _stores = [];

    public Task<Store?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_stores.SingleOrDefault(store => store.Id == id));

    public Task<PagedResult<Store>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<Store>(
            [.. _stores.OrderBy(store => store.Name, StringComparer.Ordinal).Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, _stores.Count));

    public Task<bool> TryAddAsync(Store store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (_stores.Exists(existing => string.Equals(existing.Name, store.Name, StringComparison.Ordinal)))
        {
            return Task.FromResult(false);
        }

        _stores.Add(store);
        return Task.FromResult(true);
    }
}
