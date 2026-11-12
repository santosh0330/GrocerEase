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
        Assert.Equal(3, new[] { RoleIds.Customer, RoleIds.Admin, RoleIds.StoreUser }.Distinct().Count());
        Assert.Equal(RoleNames.Customer, new Role(RoleIds.Customer, RoleNames.Customer).Name);
        Assert.Equal(RoleNames.Admin, new Role(RoleIds.Admin, RoleNames.Admin).Name);
        Assert.Equal(RoleNames.StoreUser, new Role(RoleIds.StoreUser, RoleNames.StoreUser).Name);
    }

    [Fact]
    public void ANewRole_HasNoClaims_AndGrantsNothing()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");

        Assert.Empty(role.Claims);
        Assert.Empty(role.GrantedPermissions);
        Assert.False(role.Grants(Permissions.RolesRead));
    }

    [Fact]
    public void Grant_AddsAPermissionClaim_ThatBelongsToTheRole()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");

        var claim = role.Grant(Permissions.RolesRead);

        Assert.Same(claim, Assert.Single(role.Claims));
        Assert.Equal((role.Id, Permissions.ClaimType, Permissions.RolesRead), (claim.RoleId, claim.Type, claim.Value));
        Assert.True(role.Grants(Permissions.RolesRead));
        Assert.False(role.Grants(Permissions.ProfileRead));
    }

    [Fact]
    public void GrantedPermissions_AreSortedAndLeaveOutOtherKindsOfClaim()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");
        role.Grant(Permissions.RolesRead);
        role.AddClaim("department", "kitchen"); // a claim, but not a permission
        role.Grant(Permissions.ProfileRead);

        Assert.Equal([Permissions.ProfileRead, Permissions.RolesRead], role.GrantedPermissions, StringComparer.Ordinal);
        Assert.Equal(3, role.Claims.Count);
    }

    [Fact]
    public void AddClaim_RefusesTheSameTypeAndValueTwice_ButAllowsTheSameValueUnderAnotherType()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");
        role.AddClaim("permission", "roles.read");

        Assert.Throws<ArgumentException>(() => role.AddClaim("permission", "roles.read"));
        role.AddClaim("scope", "roles.read");

        Assert.Equal(2, role.Claims.Count);
    }

    [Fact]
    public void Claims_CannotBeChangedFromOutside_ExceptThroughAddClaim()
    {
        var role = new Role(Guid.CreateVersion7(), "Tester");
        role.Grant(Permissions.ProfileRead);

        // Not the list the role keeps: a caller that casts it back to a List must not be able to add or remove a claim.
        Assert.IsNotType<List<RoleClaim>>(role.Claims);
        Assert.Throws<NotSupportedException>(() => ((IList<RoleClaim>)role.Claims).Clear());
        Assert.Single(role.Claims);
    }

    [Fact]
    public void RoleNames_All_ListsEveryRoleOnce()
    {
        Assert.Equal([RoleNames.Customer, RoleNames.Admin, RoleNames.StoreUser], RoleNames.All);
        Assert.All(RoleNames.All, name => Assert.True(name.Length <= Role.MaxNameLength));
    }
}
