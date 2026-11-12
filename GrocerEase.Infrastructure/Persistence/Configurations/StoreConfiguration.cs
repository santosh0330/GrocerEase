using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a <see cref="Store"/> maps to the <c>stores</c> table, in the same style as the others: snake_case, explicit constraint and
/// index names.
/// </summary>
public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    /// <summary>The unique index on the name. The repository recognises "name already taken" by this name.</summary>
    public const string NameIndexName = "ix_stores_name";

    public void Configure(EntityTypeBuilder<Store> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stores", table =>
            // The database refuses an id that is not a version 7 UUID, whatever wrote the row (the same rule the Store entity enforces).
            table.HasCheckConstraint("ck_stores_id_is_uuid_v7", $"uuid_extract_version(id) = {Store.RequiredIdVersion}"));

        builder.HasKey(store => store.Id).HasName("pk_stores");

        builder.Property(store => store.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the store
            .HasDefaultValueSql("uuidv7()"); // and for a row inserted by any other means, PostgreSQL 18 makes one itself

        // Stored trimmed (the entity does it). The index makes two stores with one name impossible whatever the application does;
        // the comparison is exact, so "Fresh Mart" and "fresh mart" are two stores.
        builder.Property(store => store.Name).HasColumnName("name").HasMaxLength(Store.MaxNameLength).IsRequired();
        builder.HasIndex(store => store.Name).IsUnique().HasDatabaseName(NameIndexName);

        builder.Property(store => store.Address).HasColumnName("address").HasMaxLength(Store.MaxAddressLength).IsRequired();
    }
}
