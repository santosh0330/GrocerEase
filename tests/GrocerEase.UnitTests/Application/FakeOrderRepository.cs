using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

/// <summary>
/// A list standing in for the database in handler tests. It follows the port's contract: every read names the customer (newest first) or
/// the store (oldest first), and a read hands out a <b>copy</b>, as the database does: a move made on that copy changes nothing until
/// <see cref="TryChangeStatusAsync"/> writes it, and only if the stored status is still the one expected.
/// </summary>
internal sealed class FakeOrderRepository : IOrderRepository
{
    private readonly Dictionary<Guid, OrderStatus> _statuses = [];

    /// <summary>The rows. Tests add orders here; read what is stored with <see cref="StatusOf"/>, not from these instances.</summary>
    public List<Order> Orders { get; } = [];

    /// <summary>Runs just before a status is compared and written: a test uses it to make someone else move the order first.</summary>
    public Action? BeforeChangingStatus { get; set; }

    public int StatusChanges { get; private set; }

    public OrderStatus StatusOf(Guid orderId) => _statuses.TryGetValue(orderId, out var status) ? status : Orders.Single(order => order.Id == orderId).Status;

    /// <summary>What someone else does between this request's read and its write.</summary>
    public void SomeoneElseMoves(Guid orderId, OrderStatus status) => _statuses[orderId] = status;

    private Order Copy(Order order)
    {
        var copy = Order.Place(order.Id, order.StoreId, order.CustomerId, order.DeliveryAddress, order.PlacedAt, order.Lines);
        var status = StatusOf(order.Id);
        if (status is OrderStatus.Verified or OrderStatus.Shipped)
        {
            Assert.True(copy.Verify());
        }

        if (status is OrderStatus.Shipped)
        {
            Assert.True(copy.Ship());
        }

        if (status is OrderStatus.Cancelled)
        {
            Assert.True(copy.Cancel());
        }

        return copy;
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.Where(order => order.Id == orderId && order.CustomerId == customerId).Select(Copy).SingleOrDefault());

    public Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Orders.Where(order => order.Id == orderId && order.StoreId == storeId).Select(Copy).SingleOrDefault());

    public Task<bool> TryChangeStatusAsync(Guid storeId, Guid orderId, OrderStatus expected, OrderStatus target, CancellationToken cancellationToken)
    {
        BeforeChangingStatus?.Invoke();
        if (!Orders.Exists(order => order.Id == orderId && order.StoreId == storeId) || StatusOf(orderId) != expected)
        {
            return Task.FromResult(false);
        }

        _statuses[orderId] = target;
        StatusChanges++;
        return Task.FromResult(true);
    }

    public Task<PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var ofTheStore = Orders.Where(order => order.StoreId == storeId && (status is null || StatusOf(order.Id) == status))
            .OrderBy(order => order.PlacedAt).ThenBy(order => order.Id).Select(Copy).ToList();
        return Task.FromResult(new PagedResult<Order>([.. ofTheStore.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, ofTheStore.Count));
    }

    public Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = Orders.Where(order => order.CustomerId == customerId).OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id).Select(Copy).ToList();
        return Task.FromResult(new PagedResult<Order>([.. mine.Skip((page - 1) * pageSize).Take(pageSize)], page, pageSize, mine.Count));
    }
}
