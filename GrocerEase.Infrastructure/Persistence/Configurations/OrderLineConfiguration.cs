using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How an <see cref="OrderLine"/> maps to the <c>order_lines</c> table, in the same style as the others.</summary>
public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_lines", table =>
        {
            table.HasCheckConstraint("ck_order_lines_id_is_uuid_v7", $"uuid_extract_version(id) = {OrderLine.RequiredIdVersion}");
            table.HasCheckConstraint("ck_order_lines_unit_price_is_positive", "unit_price > 0");
            table.HasCheckConstraint("ck_order_lines_quantity_in_range", $"quantity >= 1 AND quantity <= {InventoryItem.MaxQuantity}");
        });

        builder.HasKey(line => line.Id).HasName("pk_order_lines");

        builder.Property(line => line.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .HasDefaultValueSql("uuidv7()");

        builder.Property(line => line.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(line => line.ProductId).HasColumnName("product_id").IsRequired();

        // The product's name and price AS THEY WERE when it was bought: a copy, not a reference to the current ones.
        builder.Property(line => line.ProductName).HasColumnName("product_name").HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(line => line.UnitPrice).HasColumnName("unit_price").HasPrecision(12, Product.PriceScale).IsRequired();
        builder.Property(line => line.Quantity).HasColumnName("quantity").IsRequired();

        // One line per product in an order, whatever wrote the rows: a client wanting more of a product asks for a bigger quantity.
        // order_id comes first, so the index also serves "the lines of this order".
        builder.HasIndex(line => new { line.OrderId, line.ProductId }).IsUnique().HasDatabaseName("ix_order_lines_order_id_product_id");

        // Named like the others; "which orders bought this product" is the question it serves.
        builder.HasIndex(line => line.ProductId).HasDatabaseName("ix_order_lines_product_id");

        // The lines are part of the order: deleting an order deletes them. (Nothing deletes an order yet; the rule says what a line is.)
        builder.HasOne<Order>()
            .WithMany(order => order.Lines)
            .HasForeignKey(line => line.OrderId)
            .HasConstraintName("fk_order_lines_orders_order_id")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // RESTRICT: a product that was ordered cannot be deleted from under its lines (products are archived, not deleted).
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .HasConstraintName("fk_order_lines_products_product_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
