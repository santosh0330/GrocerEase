using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Persistence;

namespace GrocerEase.UnitTests.Infrastructure;

public class InMemoryUserRepositoryTests
{
    private static User NewUser(string email = "ada@example.com") =>
        new(Guid.CreateVersion7(), "Ada", "Lovelace", email, "hash");

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
        Assert.True(await repository.TryAddAsync(user, CancellationToken.None));

        var found = await repository.GetByEmailAsync("ada@example.com", CancellationToken.None);

        Assert.Same(user, found);
    }

    [Fact]
    public async Task GetByEmailAsync_IgnoresCase()
    {
        var repository = new InMemoryUserRepository();
        var user = NewUser("Ada@Example.com");
        Assert.True(await repository.TryAddAsync(user, CancellationToken.None));

        Assert.Same(user, await repository.GetByEmailAsync("ada@example.COM", CancellationToken.None));
    }

    [Fact]
    public async Task TryAddAsync_ReturnsFalseForADuplicateEmailAndKeepsTheFirstUser()
    {
        var repository = new InMemoryUserRepository();
        var first = NewUser();
        Assert.True(await repository.TryAddAsync(first, CancellationToken.None));

        Assert.False(await repository.TryAddAsync(NewUser("ADA@example.com"), CancellationToken.None));

        Assert.Same(first, await repository.GetByEmailAsync("ada@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task TryAddAsync_RejectsNull()
    {
        var repository = new InMemoryUserRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repository.TryAddAsync(null!, CancellationToken.None));
    }
}
