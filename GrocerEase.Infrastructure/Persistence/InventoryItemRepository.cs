using GrocerEase.Application.Abstractions;
using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Keeps the stock of products in PostgreSQL. Scoped, like the context it wraps. The quantity is changed by statements that do the
/// arithmetic and check the limits themselves, because a read followed by a write lets two requests that both saw "5 left" both take 5.
/// </summary>
public sealed class InventoryItemRepository(AppDbContext db) : IInventoryItemRepository
{
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

    public async Task<int?> TryAdjustAsync(Guid storeId, Guid productId, int delta, CancellationToken cancellationToken)
    {
        // EF reads the single column of a raw query as the type asked for, if the column is named "Value". RETURNING gives the new quantity,
        // and no row at all when the statement's own condition refused the change.
        var rows = delta > 0
            ? await db.Database.SqlQuery<int>($"""
                INSERT INTO inventory_items (product_id, store_id, quantity_on_hand)
                VALUES ({productId}, {storeId}, {delta})
                ON CONFLICT (product_id) DO UPDATE
                    SET quantity_on_hand = inventory_items.quantity_on_hand + {delta}
                    WHERE inventory_items.store_id = {storeId}
                      AND inventory_items.quantity_on_hand + {delta} <= {InventoryItem.MaxQuantity}
                RETURNING quantity_on_hand AS "Value"
                """).ToListAsync(cancellationToken)
            : await db.Database.SqlQuery<int>($"""
                UPDATE inventory_items
                SET quantity_on_hand = quantity_on_hand + {delta}
                WHERE product_id = {productId} AND store_id = {storeId} AND quantity_on_hand + {delta} >= 0
                RETURNING quantity_on_hand AS "Value"
                """).ToListAsync(cancellationToken);

        return rows.Count == 1 ? rows[0] : null;
    }
}
