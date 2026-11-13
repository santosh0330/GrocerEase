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
    private static readonly Guid AdminStoresManage = new("019e0000-0000-7000-8000-000000000105");
    private static readonly Guid AdminStoresRead = new("019e0000-0000-7000-8000-000000000106");
    private static readonly Guid CustomerStoresRead = new("019e0000-0000-7000-8000-000000000107");
    private static readonly Guid StoreUserStoresRead = new("019e0000-0000-7000-8000-000000000108");
    private static readonly Guid AdminProductsRead = new("019e0000-0000-7000-8000-000000000109");
    private static readonly Guid CustomerProductsRead = new("019e0000-0000-7000-8000-000000000110");
    private static readonly Guid StoreUserProductsRead = new("019e0000-0000-7000-8000-000000000111");
    private static readonly Guid StoreUserProductsManage = new("019e0000-0000-7000-8000-000000000112");
    private static readonly Guid StoreUserInventoryManage = new("019e0000-0000-7000-8000-000000000113");
    private static readonly Guid CustomerOrdersPlace = new("019e0000-0000-7000-8000-000000000114");
    private static readonly Guid CustomerOrdersRead = new("019e0000-0000-7000-8000-000000000115");
    private static readonly Guid StoreUserOrdersManage = new("019e0000-0000-7000-8000-000000000116");
    private static readonly Guid CustomerOrdersCancel = new("019e0000-0000-7000-8000-000000000117");
    private static readonly Guid AdminOrdersReadAll = new("019e0000-0000-7000-8000-000000000118");

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

        // What each role may do. Customer: read the own profile. Admin: that, and read the roles. StoreUser: read the own profile. Everyone may read stores; only the admin manages them. Everyone may browse products; only a store user manages them, and the stock of their store. Only a customer orders, reads their own orders and cancels their own pending order; only a store user moves the orders of their store; the admin reads the orders of every store, and moves none.
        builder.HasData(
            new RoleClaim(CustomerProfileRead, RoleIds.Customer, Permissions.ClaimType, Permissions.ProfileRead),
            new RoleClaim(AdminProfileRead, RoleIds.Admin, Permissions.ClaimType, Permissions.ProfileRead),
            new RoleClaim(AdminRolesRead, RoleIds.Admin, Permissions.ClaimType, Permissions.RolesRead),
            new RoleClaim(StoreUserProfileRead, RoleIds.StoreUser, Permissions.ClaimType, Permissions.ProfileRead),
            new RoleClaim(AdminStoresManage, RoleIds.Admin, Permissions.ClaimType, Permissions.StoresManage),
            new RoleClaim(AdminStoresRead, RoleIds.Admin, Permissions.ClaimType, Permissions.StoresRead),
            new RoleClaim(CustomerStoresRead, RoleIds.Customer, Permissions.ClaimType, Permissions.StoresRead),
            new RoleClaim(StoreUserStoresRead, RoleIds.StoreUser, Permissions.ClaimType, Permissions.StoresRead),
            new RoleClaim(AdminProductsRead, RoleIds.Admin, Permissions.ClaimType, Permissions.ProductsRead),
            new RoleClaim(CustomerProductsRead, RoleIds.Customer, Permissions.ClaimType, Permissions.ProductsRead),
            new RoleClaim(StoreUserProductsRead, RoleIds.StoreUser, Permissions.ClaimType, Permissions.ProductsRead),
            new RoleClaim(StoreUserProductsManage, RoleIds.StoreUser, Permissions.ClaimType, Permissions.ProductsManage),
            new RoleClaim(StoreUserInventoryManage, RoleIds.StoreUser, Permissions.ClaimType, Permissions.InventoryManage),
            new RoleClaim(CustomerOrdersPlace, RoleIds.Customer, Permissions.ClaimType, Permissions.OrdersPlace),
            new RoleClaim(CustomerOrdersRead, RoleIds.Customer, Permissions.ClaimType, Permissions.OrdersRead),
            new RoleClaim(StoreUserOrdersManage, RoleIds.StoreUser, Permissions.ClaimType, Permissions.OrdersManage),
            new RoleClaim(CustomerOrdersCancel, RoleIds.Customer, Permissions.ClaimType, Permissions.OrdersCancel),
            new RoleClaim(AdminOrdersReadAll, RoleIds.Admin, Permissions.ClaimType, Permissions.OrdersReadAll));
    }
}
