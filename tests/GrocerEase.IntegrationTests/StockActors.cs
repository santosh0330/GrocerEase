using GrocerEase.Application.Inventory.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Every stock change is written to the ledger with the user behind it, and the ledger's foreign key means that user must exist. Tests that
/// call the repository directly and do not care who made the change use one real user, made once for the run.
/// </summary>
internal static class StockActors
{
    private static readonly Lazy<Task<Guid>> Shared = new(CreateAsync);

    private static async Task<Guid> CreateAsync()
    {
        var user = new User(Guid.CreateVersion7(), "Stock", "Actor", $"stock-actor-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.StoreUser);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Configure(await PostgresServer.ApplicationConnectionStringAsync()).Options);
        Assert.True(await new UserRepository(db).TryAddAsync(user, CancellationToken.None));
        return user.Id;
    }

    /// <summary>The user behind changes made in a test that does not care who made them.</summary>
    public static Task<Guid> IdAsync() => Shared.Value;

    /// <summary>A change by hand: a delivery when it adds, a correction when it removes.</summary>
    public static async Task<StockChange> ChangeAsync(int delta) =>
        new(delta, delta > 0 ? StockMovementReason.Received : StockMovementReason.Correction, await IdAsync());
}
