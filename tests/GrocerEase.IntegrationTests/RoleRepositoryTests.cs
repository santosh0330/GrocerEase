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

        Assert.Equal([RoleNames.Admin, RoleNames.Customer], roles.Select(role => role.Name), StringComparer.Ordinal);
    }

    [Fact]
    public async Task ARoleThatWasRead_IsNotTracked()
    {
        await using var db = await NewContextAsync();

        await new RoleRepository(db).ListAsync(CancellationToken.None);

        Assert.Empty(db.ChangeTracker.Entries());
    }
}
