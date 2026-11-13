using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>Stores orders and their lines in PostgreSQL through EF Core. Scoped, like the context it wraps. Every read names the customer or the store, so someone else's order is simply not found.</summary>
public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        // The order and its lines are one aggregate: adding the order adds the lines, in the same SaveChanges and so the same transaction.
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
    }

    // No tracking on reads: an order is never changed through the context here.
    public Task<Order?> GetForCustomerAsync(Guid customerId, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == orderId && order.CustomerId == customerId, cancellationToken);

    public Task<Order?> GetForStoreAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == orderId && order.StoreId == storeId, cancellationToken);

    public async Task<PagedResult<Order>> ListForStoreAsync(Guid storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var ofTheStore = db.Orders.AsNoTracking().Where(order => order.StoreId == storeId);
        if (status is { } wanted)
        {
            ofTheStore = ofTheStore.Where(order => order.Status == wanted);
        }

        // Oldest first, with the id to break ties so the pages stay stable. Two queries, as for the customer's list.
        var total = await ofTheStore.CountAsync(cancellationToken);
        var items = await ofTheStore.Include(order => order.Lines)
            .OrderBy(order => order.PlacedAt).ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Order>(items, page, pageSize, total);
    }

    public async Task<PagedResult<Order>> ListForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var mine = db.Orders.AsNoTracking().Where(order => order.CustomerId == customerId);

        // Newest first; the id breaks ties between orders placed in the same instant, so the pages stay stable. The count and the page are
        // two queries: an order placed between them makes the total one more than the page shows, which a client refreshing absorbs.
        var total = await mine.CountAsync(cancellationToken);
        var items = await mine.Include(order => order.Lines)
            .OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Order>(items, page, pageSize, total);
    }
}
