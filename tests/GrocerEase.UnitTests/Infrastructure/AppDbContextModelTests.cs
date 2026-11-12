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
            new Dictionary<Guid, string> { [RoleIds.Customer] = RoleNames.Customer, [RoleIds.Admin] = RoleNames.Admin },
            seeded);
        Assert.Equal(RoleNames.All.Order(StringComparer.Ordinal), seeded.Values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Configure_RejectsABlankConnectionString() =>
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder<AppDbContext>().Configure(" "));
}
