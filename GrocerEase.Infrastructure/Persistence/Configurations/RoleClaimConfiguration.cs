using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>
/// How a <see cref="RoleClaim"/> maps to the <c>role_claims</c> table, in the same style as the other tables. It also seeds what
/// the two roles grant, with fixed ids.
/// </summary>
public sealed class RoleClaimConfiguration : IEntityTypeConfiguration<RoleClaim>
{
    /// <summary>One role cannot make the same statement twice.</summary>
    public const string UniqueIndexName = "ix_role_claims_role_id_claim_type_claim_value";

    // Fixed, like the ids of the roles, and for the same reason: the migration and every database must agree on them. Valid
    // version 7 UUIDs (the third group starts with 7, the fourth with 8), made readable on purpose.
    private static readonly Guid CustomerProfileRead = new("019e0000-0000-7000-8000-000000000101");
    private static readonly Guid AdminProfileRead = new("019e0000-0000-7000-8000-000000000102");
    private static readonly Guid AdminRolesRead = new("019e0000-0000-7000-8000-000000000103");
    private static readonly Guid StoreUserProfileRead = new("019e0000-0000-7000-8000-000000000104");

    public void Configure(EntityTypeBuilder<RoleClaim> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_claims", table =>
            // The database refuses an id that is not a version 7 UUID, whatever wrote the row (the same rule the entity enforces).
            table.HasCheckConstraint("ck_role_claims_id_is_uuid_v7", $"uuid_extract_version(id) = {RoleClaim.RequiredIdVersion}"));

        builder.HasKey(claim => claim.Id).HasName("pk_role_claims");

        builder.Property(claim => claim.Id)
            .HasColumnName("id")
            .ValueGeneratedNever() // the application creates the id (Guid.CreateVersion7) before it stores the claim
            .HasDefaultValueSql("uuidv7()");

        builder.Property(claim => claim.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(claim => claim.Type).HasColumnName("claim_type").HasMaxLength(RoleClaim.MaxTypeLength).IsRequired();
        builder.Property(claim => claim.Value).HasColumnName("claim_value").HasMaxLength(RoleClaim.MaxValueLength).IsRequired();

        // The unique index also serves "the claims of this role", because role_id is its first column.
        builder.HasIndex(claim => new { claim.RoleId, claim.Type, claim.Value }).IsUnique().HasDatabaseName(UniqueIndexName);

        // A claim means nothing without its role, so deleting a role deletes its claims (the opposite of users.role_id, where
        // deleting a role in use is refused). The role reaches its claims through the _claims field.
        builder.HasOne<Role>()
            .WithMany(role => role.Claims)
            .HasForeignKey(claim => claim.RoleId)
            .HasConstraintName("fk_role_claims_roles_role_id")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // What each role may do. Customer: read the own profile. Admin: that, and read the roles. StoreUser: read the own profile.
        builder.HasData(
            new RoleClaim(CustomerProfileRead, RoleIds.Customer, Permissions.ClaimType, Permissions.ProfileRead),
            new RoleClaim(AdminProfileRead, RoleIds.Admin, Permissions.ClaimType, Permissions.ProfileRead),
            new RoleClaim(AdminRolesRead, RoleIds.Admin, Permissions.ClaimType, Permissions.RolesRead),
            new RoleClaim(StoreUserProfileRead, RoleIds.StoreUser, Permissions.ClaimType, Permissions.ProfileRead));
    }
}
