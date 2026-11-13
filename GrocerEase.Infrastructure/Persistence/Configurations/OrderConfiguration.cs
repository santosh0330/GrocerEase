using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How an <see cref="Order"/> maps to the <c>orders</c> table, in the same style as the others: snake_case, explicit constraint and index names.</summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private const int StatusColumnLength = 20;

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The statuses the table accepts, from the enum itself, so the two cannot disagree.
        var statuses = string.Join(", ", Enum.GetNames<OrderStatus>().Select(name => $"'{name}'"));
        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint("ck_orders_id_is_uuid_v7", $"uuid_extract_version(id) = {Order.RequiredIdVersion}");
            table.HasCheckConstraint("ck_orders_status_is_known", $"status IN ({statuses})");
        });

        builder.HasKey(order => order.Id).HasName("pk_orders");

        builder.Property(order => order.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the order
            .HasDefaultValueSql("uuidv7()");

        builder.Property(order => order.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(order => order.CustomerId).HasColumnName("customer_id").IsRequired();

        // Stored as its name ("Pending"), so a row reads in SQL and adding a status later is not a renumbering.
        builder.Property(order => order.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(StatusColumnLength).IsRequired();
        builder.Property(order => order.DeliveryAddress).HasColumnName("delivery_address").HasMaxLength(Order.MaxDeliveryAddressLength).IsRequired();
        builder.Property(order => order.PlacedAt).HasColumnName("placed_at").IsRequired();

        // The lines live in a private field, so the aggregate can guard them. EF reads and fills that field, not a setter.
        builder.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // "My orders, newest first": the question a customer asks. The index is in that order, so the page is read straight off it.
        builder.HasIndex(order => new { order.CustomerId, order.PlacedAt, order.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_orders_customer_id_placed_at");

        // "The orders of my store, oldest first": the question a store user asks. It starts with store_id, so it also serves the foreign key.
        builder.HasIndex(order => new { order.StoreId, order.PlacedAt, order.Id }).HasDatabaseName("ix_orders_store_id_placed_at");

        // RESTRICT: a store or a customer with orders cannot be deleted from under them.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(order => order.StoreId)
            .HasConstraintName("fk_orders_stores_store_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .HasConstraintName("fk_orders_users_customer_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
