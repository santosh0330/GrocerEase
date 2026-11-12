using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Common;
using GrocerEase.Application.Stores.Commands;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure;
using GrocerEase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Why assigning a customer to a store is a transactional handler. The membership is written first and the role changed second; if
/// the role change is refused (another request changed the user in between), the membership must not stay behind. The real handler,
/// repositories and unit of work run against PostgreSQL; only the role change is made to say "overtaken", the way the real
/// compare-and-set does when it finds the user is no longer a customer.
/// </summary>
public class AssignStoreMemberRollbackTests
{
    // The real repository for everything, except that the role change is always overtaken.
    private sealed class OvertakenUserRepository(UserRepository inner) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) => inner.GetByEmailAsync(email, cancellationToken);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken) => inner.TryAddAsync(user, cancellationToken);

        public Task<bool> TryChangeRoleAsync(Guid userId, Guid fromRoleId, Guid toRoleId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static async Task<ServiceProvider> BuildAsync(bool roleChangeIsOvertaken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["ConnectionStrings:Default"] = await PostgresServer.ApplicationConnectionStringAsync(),
                ["Jwt:SecretKey"] = ApiFactory.JwtSecretKey,
                ["Jwt:Issuer"] = "GrocerEase",
                ["Jwt:Audience"] = "GrocerEase",
                ["Jwt:ExpiryMinutes"] = "10",
                ["RefreshToken:LifetimeDays"] = "3",
                ["RefreshToken:MaxLifetimeDays"] = "30",
            })
            .Build();

        var services = new ServiceCollection().AddLogging().AddApplication().AddInfrastructure(configuration);
        if (roleChangeIsOvertaken)
        {
            // Registered last, so it wins over the real one.
            services.AddScoped<IUserRepository>(provider => new OvertakenUserRepository(new UserRepository(provider.GetRequiredService<AppDbContext>())));
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<(Guid StoreId, string Email, Guid UserId)> AStoreAndACustomerAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var store = new Store(Guid.CreateVersion7(), $"Store {Guid.NewGuid():N}", "1 High Street");
        Assert.True(await scope.ServiceProvider.GetRequiredService<IStoreRepository>().TryAddAsync(store, CancellationToken.None));
        var user = new User(Guid.CreateVersion7(), "Asha", "Rai", $"asha-{Guid.NewGuid():N}@example.com", "a-password-hash", RoleIds.Customer);
        Assert.True(await new UserRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>()).TryAddAsync(user, CancellationToken.None));
        return (store.Id, user.Email, user.Id);
    }

    private static async Task<Result<StoreMemberResult>> AssignAsync(ServiceProvider provider, Guid storeId, string email)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<AssignStoreMemberCommand, Result<StoreMemberResult>>>()
            .Handle(new AssignStoreMemberCommand(storeId, email), CancellationToken.None);
    }

    private static async Task<(string Role, long Memberships)> StateOfAsync(Guid userId)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        return (
            (await PostgresServer.ScalarAsync<string>(connection, $"select r.name from users u join roles r on r.id = u.role_id where u.id = '{userId}'"))!,
            await PostgresServer.ScalarAsync<long>(connection, $"select count(*) from store_members where user_id = '{userId}'"));
    }

    [Fact]
    public async Task WhenTheRoleChangeIsOvertaken_TheMembershipThatWasWrittenBeforeItIsUndone()
    {
        await using var provider = await BuildAsync(roleChangeIsOvertaken: true);
        var (storeId, email, userId) = await AStoreAndACustomerAsync(provider);

        var result = await AssignAsync(provider, storeId, email);

        Assert.Equal("User.NotACustomer", result.Error.Code);
        Assert.Equal((RoleNames.Customer, 0L), await StateOfAsync(userId)); // no membership without the role
    }

    [Fact]
    public async Task WhenNothingGoesWrong_BothWritesAreKept()
    {
        await using var provider = await BuildAsync(roleChangeIsOvertaken: false);
        var (storeId, email, userId) = await AStoreAndACustomerAsync(provider);

        var result = await AssignAsync(provider, storeId, email);

        Assert.True(result.IsSuccess);
        Assert.Equal((RoleNames.StoreUser, 1L), await StateOfAsync(userId));
    }
}
