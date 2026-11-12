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
    /// <summary>The unique index on the email. The repository recognises "email already taken" by this name.</summary>
    public const string EmailIndexName = "ix_users_email";

    /// <summary>Names of the shadow properties for the two timestamp columns: <c>db.Entry(user).Property&lt;DateTimeOffset&gt;(name)</c>.</summary>
    public const string CreatedAtProperty = "CreatedAt";

    public const string UpdatedAtProperty = "UpdatedAt";

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
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName(EmailIndexName);

        // Not a domain rule: it is the room the hashing algorithm needs (an ASP.NET Core Identity hash is 84 characters),
        // with headroom for a stronger one later. So it lives here, with the schema, and not on the entity.
        // When a row was created and last changed. The database keeps both (a default, and a trigger for updated_at, written
        // into the migration), so the value is right whatever changes the row. They are shadow properties: they exist in the
        // model and the table, but not on the User entity, because nothing in the domain needs them.
        builder.Property<DateTimeOffset>(CreatedAtProperty)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAdd();
        builder.Property<DateTimeOffset>(UpdatedAtProperty)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .ValueGeneratedOnAddOrUpdate();

        builder.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(PasswordHashMaxLength).IsRequired();

        // Every user holds exactly one role. RESTRICT: a role that users still hold cannot be deleted, so it is never left
        // dangling, and nothing is deleted silently along with it. The index serves "which users hold this role".
        builder.Property(user => user.RoleId).HasColumnName("role_id").IsRequired();
        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .HasConstraintName("fk_users_roles_role_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
        builder.HasIndex(user => user.RoleId).HasDatabaseName("ix_users_role_id");
    }
}
