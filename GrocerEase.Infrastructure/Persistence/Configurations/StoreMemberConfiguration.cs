using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How a <see cref="StoreMember"/> maps to the <c>store_members</c> table, in the same style as the others.</summary>
public sealed class StoreMemberConfiguration : IEntityTypeConfiguration<StoreMember>
{
    /// <summary>
    /// The unique index on the user: a user runs at most one store, and the database says so whatever the application does. The
    /// repository recognises "already in a store" by this name.
    /// </summary>
    public const string UserIndexName = "ix_store_members_user_id";

    public void Configure(EntityTypeBuilder<StoreMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("store_members", table =>
            table.HasCheckConstraint("ck_store_members_id_is_uuid_v7", $"uuid_extract_version(id) = {StoreMember.RequiredIdVersion}"));

        builder.HasKey(member => member.Id).HasName("pk_store_members");

        builder.Property(member => member.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the membership
            .HasDefaultValueSql("uuidv7()");

        builder.Property(member => member.StoreId).HasColumnName("store_id").IsRequired();
        builder.Property(member => member.UserId).HasColumnName("user_id").IsRequired();

        // The pair is not the key and the user alone is unique: a store has many members, a user has one store. The index also serves
        // "which store does this user run". It is an index and not the primary key so that letting a user run several stores later is
        // one migration that drops it, and the rows keep their ids.
        builder.HasIndex(member => member.UserId).IsUnique().HasDatabaseName(UserIndexName);

        // "Who runs this store" is asked of every store route's owner; the foreign key does not index its column on its own.
        builder.HasIndex(member => member.StoreId).HasDatabaseName("ix_store_members_store_id");

        // RESTRICT on both: a store that has members, or a user who runs a store, cannot be deleted from under the membership.
        builder.HasOne<Store>()
            .WithMany()
            .HasForeignKey(member => member.StoreId)
            .HasConstraintName("fk_store_members_stores_store_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(member => member.UserId)
            .HasConstraintName("fk_store_members_users_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
