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
            [(RoleIds.Admin, RoleNames.Admin), (RoleIds.Customer, RoleNames.Customer)],
            result.Value.Select(role => (role.Id, role.Name)));
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
