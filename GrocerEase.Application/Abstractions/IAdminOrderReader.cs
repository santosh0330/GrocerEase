using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// A read-only view over the orders of <b>every</b> store, for the admin. Application owns the interface and Infrastructure implements it.
/// It is not part of <see cref="IOrderRepository"/> on purpose: every method of that port names a store or a customer, so an order can only
/// be asked for by the people it belongs to, and an architecture test holds it to that. This is the one place that reads across stores, so
/// it is its own small port that only reads, and only the use case behind <c>orders.read.all</c> uses it.
/// </summary>
public interface IAdminOrderReader
{
    /// <summary>
    /// One page of the orders of all stores, <b>newest first</b>, with their lines, optionally only those of <paramref name="storeId"/> and
    /// only those in <paramref name="status"/>. <paramref name="page"/> counts from 1; a page past the end is empty. A store that does not
    /// exist has no orders, so it is an empty page, not an error: the store is a filter here, not the thing being asked for.
    /// </summary>
    Task<PagedResult<Order>> ListAsync(Guid? storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}
