using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var user = new User(id, "Ada", "Lovelace", "ada@example.com", "s3cret!", RoleIds.Customer);

        Assert.Equal(id, user.Id);
        Assert.Equal(("Ada", "Lovelace", "ada@example.com", "s3cret!"), (user.FirstName, user.LastName, user.Email, user.PasswordHash));
    }

    [Fact]
    public void Constructor_KeepsTheRoleItWasGiven() =>
        Assert.Equal(RoleIds.Admin, new User(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", "hash", RoleIds.Admin).RoleId);

    [Fact]
    public void Constructor_RejectsAnEmptyRoleId() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new User(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.com", "hash", Guid.Empty));

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new User(Guid.Empty, "Ada", "Lovelace", "ada@example.com", "s3cret!", RoleIds.Customer));

    [Theory]
    [InlineData("", "Lovelace", "ada@example.com", "s3cret!")]
    [InlineData("Ada", " ", "ada@example.com", "s3cret!")]
    [InlineData("Ada", "Lovelace", "", "s3cret!")]
    [InlineData("Ada", "Lovelace", "ada@example.com", "")]
    public void Constructor_RejectsBlankText(string firstName, string lastName, string email, string passwordHash) =>
        Assert.Throws<ArgumentException>(() => new User(Guid.CreateVersion7(), firstName, lastName, email, passwordHash, RoleIds.Customer));

    [Fact]
    public void Constructor_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new User(Guid.CreateVersion7(), null!, "Lovelace", "ada@example.com", "s3cret!", RoleIds.Customer));

    [Fact]
    public void Constructor_AcceptsAVersion7Id()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(7, new User(id, "Ada", "Lovelace", "ada@example.com", "hash", RoleIds.Customer).Id.Version);
    }

    [Fact]
    public void Constructor_RejectsAnIdThatIsNotVersion7()
    {
        var randomId = Guid.NewGuid(); // version 4

        var exception = Assert.Throws<ArgumentException>(() => new User(randomId, "Ada", "Lovelace", "ada@example.com", "hash", RoleIds.Customer));

        Assert.Contains("version 7", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    [InlineData("  ada@example.com  ", "ada@example.com")]
    [InlineData("ADA@EXAMPLE.COM", "ada@example.com")]
    public void Constructor_StoresTheEmailTrimmedAndInLowerCase(string given, string stored) =>
        Assert.Equal(stored, new User(Guid.CreateVersion7(), "Ada", "Lovelace", given, "hash", RoleIds.Customer).Email, StringComparer.Ordinal);

    [Theory]
    [InlineData("Ada@Example.COM", "ada@example.com")]
    [InlineData("  ada@example.com ", "ada@example.com")]
    public void NormalizeEmail_AppliesTheSameRuleAsTheConstructor(string given, string normalized) =>
        Assert.Equal(normalized, User.NormalizeEmail(given), StringComparer.Ordinal);

    [Fact]
    public void NormalizeEmail_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => User.NormalizeEmail(null!));

    [Fact]
    public void Constructor_AcceptsNamesAndEmailAtTheirMaximumLength()
    {
        var name = new string('a', User.MaxNameLength);
        var email = new string('a', User.MaxEmailLength - "@x.co".Length) + "@x.co";

        var user = new User(Guid.CreateVersion7(), name, name, email, "hash", RoleIds.Customer);

        Assert.Equal(User.MaxNameLength, user.FirstName.Length);
        Assert.Equal(User.MaxEmailLength, user.Email.Length);
    }

    [Fact]
    public void Constructor_RejectsAFirstNameOverTheMaximum() =>
        Assert.Throws<ArgumentException>("firstName", () =>
            new User(Guid.CreateVersion7(), new string('a', User.MaxNameLength + 1), "Lovelace", "ada@example.com", "hash", RoleIds.Customer));

    [Fact]
    public void Constructor_RejectsALastNameOverTheMaximum() =>
        Assert.Throws<ArgumentException>("lastName", () =>
            new User(Guid.CreateVersion7(), "Ada", new string('a', User.MaxNameLength + 1), "ada@example.com", "hash", RoleIds.Customer));

    [Fact]
    public void Constructor_RejectsAnEmailOverTheMaximum() =>
        Assert.Throws<ArgumentException>("email", () =>
            new User(Guid.CreateVersion7(), "Ada", "Lovelace", new string('a', User.MaxEmailLength) + "@example.com", "hash", RoleIds.Customer));

    [Fact]
    public void Constructor_MeasuresTheEmailAfterTrimming_SoPaddingDoesNotCountAgainstTheLimit()
    {
        var email = new string('a', User.MaxEmailLength - "@x.co".Length) + "@x.co";

        var user = new User(Guid.CreateVersion7(), "Ada", "Lovelace", $"   {email}   ", "hash", RoleIds.Customer);

        Assert.Equal(User.MaxEmailLength, user.Email.Length);
    }
}
