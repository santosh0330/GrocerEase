using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.Application.Abstractions;

/// <summary>
/// Where orders are kept, with their lines. Application owns the interface and Infrastructure implements it. An order belongs to a
/// store and to the customer who placed it, so a method names whichever of the two the caller is: a customer reading their own orders is
/// scoped by <c>customerId</c>, the people who run the store by <c>storeId</c>. An architecture test says so.
/// </summary>
public interface IOrderRepository
{
    /// <summary>Stores a new order and its lines, together. Part of the unit of work that places it.</summary>
    Task AddAsync(Order order, CancellationToken cancellationToken);

    /// <summary>Finds an order of that customer, with its lines. Returns <c>null</c> when there is none, or it is someone else's.</summary>
    Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>Finds an order of that store, with its lines. Returns <c>null</c> when there is none, or it belongs to another store.</summary>
    Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the orders of that store, <b>oldest first</b> (the one waiting longest is the one to deal with first), with their lines,
    /// optionally only those in <paramref name="status"/>. <paramref name="page"/> counts from 1. A page past the end is empty.
    /// </summary>
    Task<PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the orders of that customer, newest first, with their lines. <paramref name="page"/> counts from 1. A page past the end is empty.</summary>
    Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken);
}
