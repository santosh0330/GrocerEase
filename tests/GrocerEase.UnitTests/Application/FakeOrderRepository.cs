using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>A list standing in for the database in handler tests. It follows the port's contract: every read names the customer, newest first.</summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = [];

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.SingleOrDefault(order => order.Id == orderId && order.CustomerId == customerId));

    public Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = Orders.Where(order => order.CustomerId == customerId).OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id).ToList();
        return Task.FromResult(new PagedResult<Order>([.. mine.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, mine.Count));
    }
}
