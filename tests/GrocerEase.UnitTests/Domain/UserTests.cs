using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.NewGuid();

        var user = new User(id, "Ada", "Lovelace", "ada@example.com", "s3cret!");

        Assert.Equal(id, user.Id);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com", "s3cret!"), (user.FirstName, user.LastName, user.Email, user.Password));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new User(Guid.Empty, "Ada", "Lovelace", "ada@example.com", "s3cret!"));

    [Theory]
    [InlineData("", "Lovelace", "ada@example.com", "s3cret!")]
    [InlineData("Ada", " ", "ada@example.com", "s3cret!")]
    [InlineData("Ada", "Lovelace", "", "s3cret!")]
    [InlineData("Ada", "Lovelace", "ada@example.com", "")]
    public void Constructor_RejectsBlankText(string firstName, string lastName, string email, string password) =>
        Assert.Throws<ArgumentException>(() => new User(Guid.NewGuid(), firstName, lastName, email, password));

    [Fact]
    public void Constructor_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new User(Guid.NewGuid(), null!, "Lovelace", "ada@example.com", "s3cret!"));
}
