using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class RoleClaimTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var (id, roleId) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var claim = new RoleClaim(id, roleId, "permission", "roles.read");

        Assert.Equal((id, roleId, "permission", "roles.read"), (claim.Id, claim.RoleId, claim.Type, claim.Value));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoleClaim(Guid.Empty, Guid.CreateVersion7(), "permission", "roles.read"));

    [Fact]
    public void Constructor_RejectsAnEmptyRoleId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RoleClaim(Guid.CreateVersion7(), Guid.Empty, "permission", "roles.read"));

    [Fact]
    public void Constructor_RejectsAnIdThatIsNotVersion7()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new RoleClaim(Guid.NewGuid(), Guid.CreateVersion7(), "permission", "roles.read")); // version 4

        Assert.Contains("version 7", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "roles.read")]
    [InlineData("   ", "roles.read")]
    [InlineData("permission", "")]
    [InlineData("permission", " ")]
    public void Constructor_RejectsBlankText(string type, string value) =>
        Assert.Throws<ArgumentException>(() => new RoleClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), type, value));

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RoleClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), null!, "roles.read"));
        Assert.Throws<ArgumentNullException>(() => new RoleClaim(Guid.CreateVersion7(), Guid.CreateVersion7(), "permission", null!));
    }

    [Fact]
    public void Constructor_AcceptsTheMaximumLengths_AndRejectsOneCharacterMore()
    {
        var (id, roleId) = (Guid.CreateVersion7(), Guid.CreateVersion7());

        var longest = new RoleClaim(id, roleId, new string('a', RoleClaim.MaxTypeLength), new string('b', RoleClaim.MaxValueLength));

        Assert.Equal((RoleClaim.MaxTypeLength, RoleClaim.MaxValueLength), (longest.Type.Length, longest.Value.Length));
        Assert.Throws<ArgumentException>(() => new RoleClaim(id, roleId, new string('a', RoleClaim.MaxTypeLength + 1), "x"));
        Assert.Throws<ArgumentException>(() => new RoleClaim(id, roleId, "x", new string('b', RoleClaim.MaxValueLength + 1)));
    }
}
