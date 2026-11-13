using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How a <see cref="Product"/> maps to the <c>products</c> table, in the same style as the others: snake_case, explicit constraint and index names.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <summary>
    /// The unique index on a store's live product names. A partial index: only products that are not archived count, so archiving a
    /// product frees its name. The repository recognises "name already taken" by this name.
    /// </summary>
    public const string LiveNameIndexName = "ix_products_store_id_name";

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("products", table =>
        {
            // The database refuses what the entity refuses, whatever wrote the row.
            table.HasCheckConstraint("ck_products_id_is_uuid_v7", $"uuid_extract_version(id) = {Product.RequiredIdVersion}");
            table.HasCheckConstraint("ck_products_price_is_positive", "price > 0");
        });

        builder.HasKey(product => product.Id).HasName("pk_products");

        builder.Property(product => product.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the product
            .HasDefaultValueSql("uuidv7()");

        builder.Property(product => product.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(product => product.Name).HasColumnName("name").HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(product => product.Description).HasColumnName("description").HasMaxLength(Product.MaxDescriptionLength).IsRequired();

        // numeric(12, 2): exact decimal arithmetic, never a floating point type for money.
        builder.Property(product => product.Price).HasColumnName("price").HasPrecision(12, Product.PriceScale).IsRequired();
        builder.Property(product => product.ArchivedAt).HasColumnName("archived_at");

        // store_id first, so it also serves "the products of this store". The filter is what makes archiving free the name.
        builder.HasIndex(product => new { product.StoreId, product.Name })
            .IsUnique()
            .HasFilter("archived_at IS NULL")
            .HasDatabaseName(LiveNameIndexName);

        // The target of a composite foreign key: an inventory item points at (product, store), so it cannot claim a store its product is not in.
        // The id alone is already unique; this says that the pair is too, which a foreign key needs.
        builder.HasAlternateKey(product => new { product.Id, product.StoreId }).HasName("uq_products_id_store_id");

        // RESTRICT: a store that has products cannot be deleted from under them.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(product => product.StoreId)
            .HasConstraintName("fk_products_stores_store_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
