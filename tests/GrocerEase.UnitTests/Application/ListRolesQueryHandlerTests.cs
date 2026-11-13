using GrocerEase.Application.Roles.Handlers;
using GrocerEase.Application.Roles.Queries;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class ListRolesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsEveryRoleWithItsIdAndName_OrderedByName()
    {
        var handler = new ListRolesQueryHandler(new FakeRoleRepository());

        var result = await handler.Handle(new ListRolesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [(RoleIds.Admin, RoleNames.Admin), (RoleIds.Customer, RoleNames.Customer), (RoleIds.StoreUser, RoleNames.StoreUser)],
            result.Value.Select(role => (role.Id, role.Name)));
    }

    [Fact]
    public async Task Handle_ReturnsWhatEachRoleGrants_InAStableOrder()
    {
        var handler = new ListRolesQueryHandler(new FakeRoleRepository());

        var result = await handler.Handle(new ListRolesQuery(), CancellationToken.None);

        var byName = result.Value.ToDictionary(role => role.Name, role => role.Permissions, StringComparer.Ordinal);
        Assert.Equal([Permissions.ProductsRead, Permissions.ProfileRead, Permissions.RolesRead, Permissions.StoresManage, Permissions.StoresRead], byName[RoleNames.Admin], StringComparer.Ordinal);
        Assert.Equal([Permissions.OrdersPlace, Permissions.OrdersRead, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], byName[RoleNames.Customer], StringComparer.Ordinal);
        Assert.Equal([Permissions.InventoryManage, Permissions.ProductsManage, Permissions.ProductsRead, Permissions.ProfileRead, Permissions.StoresRead], byName[RoleNames.StoreUser], StringComparer.Ordinal);
    }

    [Fact]
    public async Task Handle_WhenThereAreNoRoles_IsAnEmptyListNotAnError()
    {
        var handler = new ListRolesQueryHandler(new FakeRoleRepository(seeded: false));

        var result = await handler.Handle(new ListRolesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Handle_RejectsNull()
    {
        var handler = new ListRolesQueryHandler(new FakeRoleRepository());

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.Handle(null!, CancellationToken.None));
    }
}
