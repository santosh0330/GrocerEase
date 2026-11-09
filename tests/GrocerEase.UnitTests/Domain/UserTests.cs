using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var user = new User(id, "Ada", "Lovelace", "ada@example.com", "s3cret!");

        Assert.Equal(id, user.Id);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com", "s3cret!"), (user.FirstName, user.LastName, user.Email, user.PasswordHash));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new User(Guid.Empty, "Ada", "Lovelace", "ada@example.com", "s3cret!"));

    [Theory]
    [InlineData("", "Lovelace", "ada@example.com", "s3cret!")]
    [InlineData("Ada", " ", "ada@example.com", "s3cret!")]
    [InlineData("Ada", "Lovelace", "", "s3cret!")]
    [InlineData("Ada", "Lovelace", "ada@example.com", "")]
    public void Constructor_RejectsBlankText(string firstName, string lastName, string email, string passwordHash) =>
        Assert.Throws<ArgumentException>(() => new User(Guid.CreateVersion7(), firstName, lastName, email, passwordHash));

    [Fact]
    public void Constructor_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new User(Guid.CreateVersion7(), null!, "Lovelace", "ada@example.com", "s3cret!"));

    [Fact]
    public void Constructor_AcceptsAVersion7Id()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(7, new User(id, "Ada", "Lovelace", "ada@example.com", "hash").Id.Version);
    }

    [Fact]
    public void Constructor_RejectsAnIdThatIsNotVersion7()
    {
        var randomId = Guid.NewGuid(); // version 4

        var exception = Assert.Throws<ArgumentException>(() => new User(randomId, "Ada", "Lovelace", "ada@example.com", "hash"));

        Assert.Contains("version 7", exception.Message, StringComparison.Ordinal);
    }
}
