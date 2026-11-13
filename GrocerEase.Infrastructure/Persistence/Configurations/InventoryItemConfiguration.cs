using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How an <see cref="InventoryItem"/> maps to the <c>inventory_items</c> table, in the same style as the others.</summary>
public sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The last line of defence: the statements that change the quantity refuse to go outside these limits, and the table refuses it
        // whatever wrote the row.
        builder.ToTable("inventory_items", table =>
        {
            table.HasCheckConstraint(
                "ck_inventory_items_quantity_in_range", $"quantity_on_hand >= 0 AND quantity_on_hand <= {InventoryItem.MaxQuantity}");
            // No level at all is allowed (null: never low); a level, when there is one, is in the same range as a quantity.
            table.HasCheckConstraint(
                "ck_inventory_items_low_stock_threshold_in_range",
                $"low_stock_threshold IS NULL OR (low_stock_threshold >= 0 AND low_stock_threshold <= {InventoryItem.MaxLowStockThreshold})");
        });

        // One row per product, so the product is the key. No generated id: nothing else refers to an inventory item.
        builder.HasKey(item => item.ProductId).HasName("pk_inventory_items");
        builder.Property(item => item.ProductId).HasColumnName("product_id").ValueGeneratedNever();

        builder.Property(item => item.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(item => item.QuantityOnHand).HasColumnName("quantity_on_hand").IsRequired();
        builder.Property(item => item.LowStockThreshold).HasColumnName("low_stock_threshold"); // nullable: not every product has a level

        // "The stock of this store": the question the inventory list and every store-scoped statement asks.
        builder.HasIndex(item => item.StoreId).HasDatabaseName("ix_inventory_items_store_id");

        // EF makes an index for the foreign key's columns whether or not one is wanted; the primary key on the product already serves every
        // lookup by product, so this one is not needed. It is named, like every other, so the schema reads the same throughout.
        builder.HasIndex(item => new { item.ProductId, item.StoreId }).HasDatabaseName("ix_inventory_items_product_id_store_id");

        // The composite foreign key: (product, store) must be a product and the store it belongs to. The database, not the application,
        // guarantees an item and its product agree about the store. RESTRICT: a product with stock cannot be deleted from under it.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(item => new { item.ProductId, item.StoreId })
            .HasPrincipalKey(product => new { product.Id, product.StoreId })
            .HasConstraintName("fk_inventory_items_products_product_id_store_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
