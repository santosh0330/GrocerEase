using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;

namespace GrocerEase.UnitTests.Infrastructure;

public class InMemoryUserRepositoryTests
{
    private static User NewUser(string email = "ada@example.com") =>
        new(Guid.NewGuid(), "Ada", "Lovelace", email, "hash");

    [Fact]
    public async Task GetByEmailAsync_ReturnsNullWhenThereIsNoUser()
    {
        var repository = new InMemoryUserRepository();

        Assert.Null(await repository.GetByEmailAsync("ada@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task GetByEmailAsync_ReturnsTheUserThatWasAdded()
    {
        var repository = new InMemoryUserRepository();
        var user = NewUser();
        await repository.AddAsync(user, CancellationToken.None);

        var found = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);

        Assert.Same(user, found);
    }

    [Fact]
    public async Task GetByEmailAsync_IgnoresCase()
    {
        var repository = new InMemoryUserRepository();
        var user = NewUser("Ada@Example.com");
        await repository.AddAsync(user, CancellationToken.None);

        Assert.Same(user, await repository.GetByEmailAsync("ada@example.COM", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_ThrowsForADuplicateEmailAndKeepsTheFirstUser()
    {
        var repository = new InMemoryUserRepository();
        var first = NewUser();
        await repository.AddAsync(first, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(NewUser("ADA@example.com"), CancellationToken.None));

        Assert.Same(first, await repository.GetByEmailAsync("ada@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_RejectsNull()
    {
        var repository = new InMemoryUserRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.AddAsync(null!, CancellationToken.None));
    }
}
