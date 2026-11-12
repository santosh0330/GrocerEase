using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class RoleTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var role = new Role(id, "Customer");

        Assert.Equal((id, "Customer"), (role.Id, role.Name));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Role(Guid.Empty, "Customer"));

    [Fact]
    public void Constructor_RejectsAnIdThatIsNotVersion7()
    {
        var randomId = Guid.NewGuid(); // version 4

        var exception = Assert.Throws<ArgumentException>(() => new Role(randomId, "Customer"));

        Assert.Contains("version 7", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankName(string name) =>
        Assert.Throws<ArgumentException>(() => new Role(Guid.CreateVersion7(), name));

    [Fact]
    public void Constructor_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => new Role(Guid.CreateVersion7(), null!));

    [Fact]
    public void Constructor_AcceptsANameOfTheMaximumLength_AndRejectsOneCharacterMore()
    {
        Assert.Equal(Role.MaxNameLength, new Role(Guid.CreateVersion7(), new string('a', Role.MaxNameLength)).Name.Length);
        Assert.Throws<ArgumentException>(() => new Role(Guid.CreateVersion7(), new string('a', Role.MaxNameLength + 1)));
    }

    [Fact]
    public void TheSeededIds_AreDistinctVersion7Ids_ThatBuildTheSeededRoles()
    {
        Assert.NotEqual(RoleIds.Customer, RoleIds.Admin);
        Assert.Equal(RoleNames.Customer, new Role(RoleIds.Customer, RoleNames.Customer).Name);
        Assert.Equal(RoleNames.Admin, new Role(RoleIds.Admin, RoleNames.Admin).Name);
    }

    [Fact]
    public void RoleNames_All_ListsEveryRoleOnce()
    {
        Assert.Equal([RoleNames.Customer, RoleNames.Admin], RoleNames.All);
        Assert.All(RoleNames.All, name => Assert.True(name.Length <= Role.MaxNameLength));
    }
}
