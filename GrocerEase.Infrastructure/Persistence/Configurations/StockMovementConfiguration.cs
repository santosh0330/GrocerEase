using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a <see cref="StockMovement"/> maps to the <c>stock_movements</c> table, in the same style as the others. The table is the ledger of
/// the stock: append-only, so a migration also takes <c>UPDATE</c> and <c>DELETE</c> away from the application's database role.
/// </summary>
public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    private const int ReasonColumnLength = 20;

    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureTable(builder);
        ConfigureColumns(builder);
        ConfigureIndexes(builder);
        ConfigureRelationships(builder);
    }

    private static void ConfigureTable(EntityTypeBuilder<StockMovement> builder)
    {
        // The reasons the table accepts, from the enum itself, so the two cannot disagree.
        var reasons = string.Join(", ", Enum.GetNames<StockMovementReason>().Select(name => $"'{name}'"));
        builder.ToTable("stock_movements", table =>
        {
            table.HasCheckConstraint("ck_stock_movements_id_is_uuid_v7", $"uuid_extract_version(id) = {StockMovement.RequiredIdVersion}");
            table.HasCheckConstraint("ck_stock_movements_reason_is_known", $"reason IN ({reasons})");
            table.HasCheckConstraint("ck_stock_movements_delta_in_range", $"delta <> 0 AND delta >= -{InventoryItem.MaxQuantity} AND delta <= {InventoryItem.MaxQuantity}");

            // The same rules as the entity, so a row that makes no sense is refused whatever wrote it: a delivery, a cancellation and the
            // opening balance add; an order removes; a correction may do either.
            table.HasCheckConstraint(
                "ck_stock_movements_delta_matches_reason",
                "(reason = 'Correction') OR (reason = 'OrderPlaced' AND delta < 0) OR (reason IN ('OpeningBalance', 'Received', 'OrderCancelled') AND delta > 0)");

            // Only an order's movements name an order; only the opening balance has no actor.
            table.HasCheckConstraint("ck_stock_movements_order_id_matches_reason", "(reason IN ('OrderPlaced', 'OrderCancelled')) = (order_id IS NOT NULL)");
            table.HasCheckConstraint("ck_stock_movements_actor_id_matches_reason", "(reason = 'OpeningBalance') = (actor_id IS NULL)");
        });
    }

    private static void ConfigureColumns(EntityTypeBuilder<StockMovement> builder)
    {
        builder.HasKey(movement => movement.Id).HasName("pk_stock_movements");

        builder.Property(movement => movement.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) in the statement that changes the stock
            .HasDefaultValueSql("uuidv7()");

        builder.Property(movement => movement.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(movement => movement.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(movement => movement.Delta).HasColumnName("delta").IsRequired();
        builder.Property(movement => movement.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(ReasonColumnLength).IsRequired();
        builder.Property(movement => movement.ActorId).HasColumnName("actor_id");
        builder.Property(movement => movement.OrderId).HasColumnName("order_id");
        builder.Property(movement => movement.CreatedAt).HasColumnName("created_at").IsRequired();
    }

    private static void ConfigureIndexes(EntityTypeBuilder<StockMovement> builder)
    {
        // "The history of this product, newest first": the question the ledger is read for. The index is in that order, so a page is read
        // straight off it.
        builder.HasIndex(movement => new { movement.ProductId, movement.CreatedAt, movement.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_stock_movements_product_id_created_at");

        // EF makes an index for each foreign key's columns whether or not one is wanted; they are named like the others.
        builder.HasIndex(movement => new { movement.ProductId, movement.StoreId }).HasDatabaseName("ix_stock_movements_product_id_store_id");
        builder.HasIndex(movement => movement.ActorId).HasDatabaseName("ix_stock_movements_actor_id");
        builder.HasIndex(movement => movement.OrderId).HasDatabaseName("ix_stock_movements_order_id");
    }

    private static void ConfigureRelationships(EntityTypeBuilder<StockMovement> builder)
    {
        // The composite foreign key: (product, store) must be a product and the store it belongs to, as for the stock row. RESTRICT: a product
        // with a history cannot be deleted from under it.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(movement => new { movement.ProductId, movement.StoreId })
            .HasPrincipalKey(product => new { product.Id, product.StoreId })
            .HasConstraintName("fk_stock_movements_products_product_id_store_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Nobody who made a change is deleted from under it, and nor is an order that moved stock. The order's foreign key is made
        // DEFERRABLE INITIALLY DEFERRED by the migration (EF cannot say so): an order's stock is taken *before* the order row is written,
        // in the same transaction, and the key is checked when the transaction commits.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(movement => movement.ActorId)
            .HasConstraintName("fk_stock_movements_users_actor_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(movement => movement.OrderId)
            .HasConstraintName("fk_stock_movements_orders_order_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
