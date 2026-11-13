using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GrocerEase.UnitTests.Infrastructure;

public class AppDbContextModelTests
{
    // No connection is opened: these tests only look at the model, which needs no database.
    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure("Host=localhost;Database=never_connected").Options);

    [Fact]
    public void TheModelAndTheMigrationsAgree_SoNoMigrationIsMissing()
    {
        using var db = Context();

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The model changed since the last migration. Run: dotnet ef migrations add <Name> --project GrocerEase.Infrastructure --output-dir Persistence/Migrations");
    }

    [Fact]
    public void TheRolesTable_IsSeededWithEveryRoleName_UnderItsFixedId()
    {
        using var db = Context();

        // Seed data is design-time information: the runtime model that db.Model returns does not keep it.
        var seeded = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Role))!.GetSeedData()
            .ToDictionary(row => (Guid)row[nameof(Role.Id)]!, row => (string)row[nameof(Role.Name)]!);

        Assert.Equal(
            new Dictionary<Guid, string>
            {
                [RoleIds.Customer] = RoleNames.Customer,
                [RoleIds.Admin] = RoleNames.Admin,
                [RoleIds.StoreUser] = RoleNames.StoreUser,
            },
            seeded);
        Assert.Equal(RoleNames.All.Order(StringComparer.Ordinal), seeded.Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRoleClaimsTable_IsSeededWithWhatEachRoleMayDo()
    {
        using var db = Context();

        var claims = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(RoleClaim))!.GetSeedData()
            .Select(row => (RoleId: (Guid)row[nameof(RoleClaim.RoleId)]!, Type: (string)row[nameof(RoleClaim.Type)]!, Value: (string)row[nameof(RoleClaim.Value)]!))
            .ToList();

        // What each role may do, in the order the claims sort: by value.
        static List<string> Of(IEnumerable<(Guid RoleId, string Type, string Value)> claims, Guid roleId) =>
            [.. claims.Where(claim => claim.RoleId == roleId && string.Equals(claim.Type, Permissions.ClaimType, StringComparison.Ordinal))
                .Select(claim => claim.Value).Order(StringComparer.Ordinal)];

        Assert.Equal([Permissions.OrdersCancel, Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], Of(claims, RoleIds.Customer), StringComparer.Ordinal);
        Assert.Equal(
            [Permissions.OrdersReadAll, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresManage, Permissions.StoresRead],
            Of(claims, RoleIds.Admin),
            StringComparer.Ordinal);
        Assert.Equal(
            [Permissions.InventoryManage, Permissions.OrdersManage, Permissions.ProductsManage, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead],
            Of(claims, RoleIds.StoreUser),
            StringComparer.Ordinal);
        Assert.Equal(18, claims.Count);
    }

    [Fact]
    public void ARolesClaims_AreDeletedWithTheRole()
    {
        using var db = Context();

        var foreignKey = Assert.Single(
            db.Model.FindEntityType(typeof(RoleClaim))!.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Role));

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal([nameof(RoleClaim.RoleId)], foreignKey.Properties.Select(property => property.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void EveryUserHoldsOneRole_AndARoleThatIsHeldCannotBeDeleted()
    {
        using var db = Context();

        var foreignKey = Assert.Single(
            db.Model.FindEntityType(typeof(User))!.GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Role));

        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Equal([nameof(User.RoleId)], foreignKey.Properties.Select(property => property.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void Configure_RejectsABlankConnectionString() =>
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder<AppDbContext>().Configure(" "));
}
