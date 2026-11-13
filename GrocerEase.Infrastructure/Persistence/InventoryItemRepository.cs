using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Keeps the stock of products in PostgreSQL. Scoped, like the context it wraps. The quantity is changed by statements that do the
/// arithmetic and check the limits themselves, because a read followed by a write lets two requests that both saw "5 left" both take 5.
/// </summary>
public sealed class InventoryItemRepository(AppDbContext db, TimeProvider? clock = null) : IInventoryItemRepository
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public async Task<IReadOnlyDictionary<Guid, InventoryItem>> GetItemsAsync(Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        // No tracking: stock is changed by statements, never through the context. The store is in the query as well as the ids.
        return await db.InventoryItems.AsNoTracking()
            .Where(item => item.StoreId == storeId && productIds.Contains(item.ProductId))
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
    }

    public async Task<int?> SetLowStockThresholdAsync(Guid storeId, Guid productId, int? threshold, CancellationToken cancellationToken)
    {
        // CAST: a null level is sent without a type, and PostgreSQL must be told what it is. On a row that exists only the level is written,
        // and only if the row is in this store (the composite foreign key already ties the two, so this is a second lock on the same door).
        var rows = await db.Database.SqlQuery<int>($"""
            INSERT INTO inventory_items (product_id, store_id, quantity_on_hand, low_stock_threshold)
            VALUES ({productId}, {storeId}, 0, CAST({threshold} AS integer))
            ON CONFLICT (product_id) DO UPDATE
                SET low_stock_threshold = CAST({threshold} AS integer)
                WHERE inventory_items.store_id = {storeId}
            RETURNING quantity_on_hand AS "Value"
            """).ToListAsync(cancellationToken);

        return rows.Count == 1 ? rows[0] : null;
    }

    public async Task<int?> TryAdjustAsync(Guid storeId, Guid productId, StockChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        // A stock change with no movement is the bug the ledger exists to catch, so the shape of one is checked before any SQL.
        if (change.Reason == StockMovementReason.OpeningBalance)
        {
            throw new ArgumentException("The opening balance is written once, by the migration that made the ledger.", nameof(change));
        }

        var delta = change.Delta;
        var movementId = Guid.CreateVersion7();
        var reason = change.Reason.ToString();
        var now = timeProvider.GetUtcNow();
        _ = new StockMovement(movementId, productId, storeId, delta, change.Reason, change.ActorId, change.OrderId, now); // refuses a movement that makes no sense

        // EF reads the single column of a raw query as the type asked for, if the column is named "Value". The quantity is changed by the first
        // CTE ("changed") and RETURNING gives the new quantity, and no row at all when the statement's own condition refused the change. The
        // second CTE ("moved") writes the movement for exactly the rows "changed" returned: a refused change writes nothing. PostgreSQL runs a
        // data-modifying CTE exactly once and to completion whether or not the main query reads it, so the two writes are one statement.
        // CAST: a null order is sent without a type, and PostgreSQL must be told what it is.
        var rows = delta > 0
            ? await db.Database.SqlQuery<int>($"""
                WITH changed AS (
                    INSERT INTO inventory_items (product_id, store_id, quantity_on_hand)
                    VALUES ({productId}, {storeId}, {delta})
                    ON CONFLICT (product_id) DO UPDATE
                        SET quantity_on_hand = inventory_items.quantity_on_hand + {delta}
                        WHERE inventory_items.store_id = {storeId}
                          AND inventory_items.quantity_on_hand + {delta} <= {InventoryItem.MaxQuantity}
                    RETURNING quantity_on_hand),
                moved AS (
                    INSERT INTO stock_movements (id, product_id, store_id, delta, reason, actor_id, order_id, created_at)
                    SELECT {movementId}, {productId}, {storeId}, {delta}, {reason}, {change.ActorId}, CAST({change.OrderId} AS uuid), {now}
                    FROM changed)
                SELECT quantity_on_hand AS "Value" FROM changed
                """).ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>($"""
                WITH changed AS (
                    UPDATE inventory_items
                    SET quantity_on_hand = quantity_on_hand + {delta}
                    WHERE product_id = {productId} AND store_id = {storeId} AND quantity_on_hand + {delta} >= 0
                    RETURNING quantity_on_hand),
                moved AS (
                    INSERT INTO stock_movements (id, product_id, store_id, delta, reason, actor_id, order_id, created_at)
                    SELECT {movementId}, {productId}, {storeId}, {delta}, {reason}, {change.ActorId}, CAST({change.OrderId} AS uuid), {now}
                    FROM changed)
                SELECT quantity_on_hand AS "Value" FROM changed
                """).ToListAsync(cancellationToken);

        return rows.Count == 1 ? rows[0] : null;
    }

    public async Task<PagedResult<StockMovement>> ListMovementsAsync(Guid storeId, Guid productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var history = db.StockMovements.AsNoTracking().Where(movement => movement.StoreId == storeId && movement.ProductId == productId);

        // Newest first; the id breaks ties between movements made in the same instant (ids are version 7, so they sort by creation), so the
        // pages stay stable. Two queries, as for every list: a movement made between them makes the total one more than the page shows.
        var total = await history.CountAsync(cancellationToken);
        var items = await history.OrderByDescending(movement => movement.CreatedAt).ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<StockMovement>(items, page, pageSize, total);
    }
}
