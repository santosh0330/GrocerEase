using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a <see cref="User"/> maps to the <c>users</c> table. Names are written out (snake_case, explicit constraint and
/// index names) so the schema reads naturally in SQL and does not change if an EF naming convention does.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    private const int PasswordHashMaxLength = 256;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users", table =>
            // The database refuses an id that is not a version 7 UUID, whatever wrote the row (the same rule the User entity enforces).
            table.HasCheckConstraint("ck_users_id_is_uuid_v7", $"uuid_extract_version(id) = {User.RequiredIdVersion}"));

        builder.HasKey(user => user.Id).HasName("pk_users");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the user
            .HasDefaultValueSql("uuidv7()"); // and for a row inserted by any other means, PostgreSQL 18 makes one itself

        builder.Property(user => user.FirstName).HasColumnName("first_name").HasMaxLength(User.MaxNameLength).IsRequired();
        builder.Property(user => user.LastName).HasColumnName("last_name").HasMaxLength(User.MaxNameLength).IsRequired();

        // Stored trimmed and lower case (User.NormalizeEmail), so a plain unique index is a case-insensitive uniqueness rule.
        builder.Property(user => user.Email).HasColumnName("email").HasMaxLength(User.MaxEmailLength).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ix_users_email");

        // Not a domain rule: it is the room the hashing algorithm needs (an ASP.NET Core Identity hash is 84 characters),
        // with headroom for a stronger one later. So it lives here, with the schema, and not on the entity.
        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(PasswordHashMaxLength).IsRequired();
    }
}
