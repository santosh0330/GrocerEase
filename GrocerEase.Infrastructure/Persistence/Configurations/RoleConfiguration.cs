using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a <see cref="Role"/> maps to the <c>roles</c> table, in the same style as the users table: snake_case, explicit
/// constraint and index names. It also seeds the two roles the application knows, with fixed ids.
/// </summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public const string NameIndexName = "ix_roles_name";

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles", table =>
            // The database refuses an id that is not a version 7 UUID, whatever wrote the row (the same rule the Role entity enforces).
            table.HasCheckConstraint("ck_roles_id_is_uuid_v7", $"uuid_extract_version(id) = {Role.RequiredIdVersion}"));

        builder.HasKey(role => role.Id).HasName("pk_roles");

        builder.Property(role => role.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // roles are seeded with fixed ids; the application never creates one
            .HasDefaultValueSql("uuidv7()"); // and for a row inserted by any other means, PostgreSQL 18 makes one itself

        // A role is found by its name (a new user gets "Customer"), and two roles with one name would make that ambiguous.
        builder.Property(role => role.Name).HasColumnName("name").HasMaxLength(Role.MaxNameLength).IsRequired();
        builder.HasIndex(role => role.Name).IsUnique().HasDatabaseName(NameIndexName);

        // The claims live in a private field, so the entity can guard them (AddClaim). EF reads and fills that field, not a setter.
        builder.Navigation(role => role.Claims).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Part of the migration that creates the table, so every database starts with the same two roles under the same ids.
        builder.HasData(
            new Role(RoleIds.Customer, RoleNames.Customer),
            new Role(RoleIds.Admin, RoleNames.Admin));
    }
}
