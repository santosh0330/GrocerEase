using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
    public void Configure_RejectsABlankConnectionString() =>
        Assert.Throws<ArgumentException>(() => new DbContextOptionsBuilder<AppDbContext>().Configure(" "));
}
