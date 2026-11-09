using GrocerEase.Domain.Entities;
using GrocerEase.Infrastructure.Authentication;

namespace GrocerEase.UnitTests.Infrastructure;

public class PlaceholderJwtTokenGeneratorTests
{
    [Fact]
    public void GenerateToken_ReturnsANonEmptyToken()
    {
        var generator = new PlaceholderJwtTokenGenerator();

        var token = generator.GenerateToken(new User(Guid.NewGuid(), "Ada", "Lovelace", "ada@example.com", "hash"));

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void GenerateToken_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new PlaceholderJwtTokenGenerator().GenerateToken(null!));
}
