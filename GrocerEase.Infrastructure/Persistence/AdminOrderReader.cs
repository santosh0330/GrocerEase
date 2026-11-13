using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Reads the orders of every store through EF Core, for the admin. Scoped, like the context it wraps. It reads and writes nothing else:
/// no tracking, no <c>SaveChanges</c>. It is the one class besides <see cref="OrderRepository"/> that touches the orders table, and an
/// architecture test names it.
/// </summary>
public sealed class AdminOrderReader(AppDbContext db) : IAdminOrderReader
{
    public async Task<PagedResult<Order>> ListAsync(Guid? storeId, OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var orders = db.Orders.AsNoTracking();
        if (storeId is { } store)
        {
            orders = orders.Where(order => order.StoreId == store);
        }

        if (status is { } wanted)
        {
            orders = orders.Where(order => order.Status == wanted);
        }

        // Newest first, with the id to break ties so the pages stay stable. Two queries, as for the other lists.
        var total = await orders.CountAsync(cancellationToken);
        var items = await orders.Include(order => order.Lines)
            .OrderByDescending(order => order.PlacedAt).ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<Order>(items, page, pageSize, total);
    }
}
