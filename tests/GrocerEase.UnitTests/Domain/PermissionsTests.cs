using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class PermissionsTests
{
    [Fact]
    public void All_ListsEveryPermissionOnce() =>
        Assert.Equal([Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresRead, Permissions.StoresManage, Permissions.ProductsRead, Permissions.ProductsManage, Permissions.InventoryManage, Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.OrdersManage],
            Permissions.All, StringComparer.Ordinal);

    [Fact]
    public void EveryPermission_FitsTheColumnThatStoresIt()
    {
        Assert.All(Permissions.All, permission => Assert.InRange(permission.Length, 1, RoleClaim.MaxValueLength));
        Assert.InRange(Permissions.ClaimType.Length, 1, RoleClaim.MaxTypeLength);
    }

    [Fact]
    public void EveryPermission_IsAClaimTheRoleEntityAccepts()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");

        Assert.All(Permissions.All, permission => role.Grant(permission));

        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), role.GrantedPermissions, StringComparer.Ordinal);
    }
}
