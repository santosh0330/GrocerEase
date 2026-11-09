using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// Lets the <c>dotnet ef</c> tool create the context without starting the web app. Adding a migration needs no database;
/// applying one gets the connection string from <c>ConnectionStrings__Default</c> or the tool's <c>--connection</c> option.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=grocerease_design_time"; // only used to pick the provider; nothing connects to it

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Configure(connectionString).Options);
    }
}
