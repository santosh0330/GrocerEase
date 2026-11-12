using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// The role repository against a real PostgreSQL, connected as the least-privilege application role, exactly as the API is.
/// It reads the two seeded roles, so no test needs to add anything.
/// </summary>
public class RoleRepositoryTests
{
    private static async Task<AppDbContext> NewContextAsync() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);

    [Fact]
    public async Task ARoleIsFoundByName_WithItsFixedId()
    {
        await using var db = await NewContextAsync();

        var customer = await new RoleRepository(db).GetByNameAsync(RoleNames.Customer, CancellationToken.None);

        Assert.NotNull(customer);
        Assert.Equal((RoleIds.Customer, RoleNames.Customer), (customer.Id, customer.Name));
    }

    [Fact]
    public async Task ARoleIsFoundById()
    {
        await using var db = await NewContextAsync();

        var admin = await new RoleRepository(db).GetByIdAsync(RoleIds.Admin, CancellationToken.None);

        Assert.Equal(RoleNames.Admin, admin?.Name);
    }

    [Fact]
    public async Task AnUnknownIdOrName_IsNull()
    {
        await using var db = await NewContextAsync();
        var repository = new RoleRepository(db);

        Assert.Null(await repository.GetByIdAsync(Guid.CreateVersion7(), CancellationToken.None));
        Assert.Null(await repository.GetByNameAsync("Nobody", CancellationToken.None));
    }

    [Fact]
    public async Task TheNameLookup_IsExact_SoADifferentCaseIsAnotherRole()
    {
        await using var db = await NewContextAsync();

        Assert.Null(await new RoleRepository(db).GetByNameAsync("customer", CancellationToken.None));
    }

    [Fact]
    public async Task TheList_HoldsEveryRoleOrderedByName()
    {
        await using var db = await NewContextAsync();

        var roles = await new RoleRepository(db).ListAsync(CancellationToken.None);

        Assert.Equal([RoleNames.Admin, RoleNames.Customer, RoleNames.StoreUser], roles.Select(role => role.Name), StringComparer.Ordinal);
    }

    [Fact]
    public async Task EveryRoleComesBackWithWhatItGrants_ByName_ById_AndInTheList()
    {
        await using var db = await NewContextAsync();
        var repository = new RoleRepository(db);

        var customer = await repository.GetByNameAsync(RoleNames.Customer, CancellationToken.None);
        var admin = await repository.GetByIdAsync(RoleIds.Admin, CancellationToken.None);
        var listed = await repository.ListAsync(CancellationToken.None);

        Assert.Equal([Permissions.ProfileRead], customer?.GrantedPermissions, StringComparer.Ordinal);
        Assert.Equal([Permissions.ProfileRead, Permissions.RolesRead], admin?.GrantedPermissions, StringComparer.Ordinal);
        Assert.Equal(
            [Permissions.ProfileRead, Permissions.RolesRead],
            listed.Single(role => string.Equals(role.Name, RoleNames.Admin, StringComparison.Ordinal)).GrantedPermissions,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task ACustomerDoesNotGrantTheRolesPermission_AndAnAdminDoes()
    {
        await using var db = await NewContextAsync();
        var repository = new RoleRepository(db);

        Assert.False((await repository.GetByNameAsync(RoleNames.Customer, CancellationToken.None))!.Grants(Permissions.RolesRead));
        Assert.True((await repository.GetByNameAsync(RoleNames.Admin, CancellationToken.None))!.Grants(Permissions.RolesRead));
    }

    [Fact]
    public async Task TheClaims_BelongToTheRoleTheyWereReadWith()
    {
        await using var db = await NewContextAsync();

        var admin = await new RoleRepository(db).GetByNameAsync(RoleNames.Admin, CancellationToken.None);

        Assert.All(admin!.Claims, claim => Assert.Equal(admin.Id, claim.RoleId));
        Assert.All(admin.Claims, claim => Assert.Equal(Permissions.ClaimType, claim.Type));
    }

    [Fact]
    public async Task ARoleThatWasRead_IsNotTracked()
    {
        await using var db = await NewContextAsync();

        await new RoleRepository(db).ListAsync(CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries());
    }
}
