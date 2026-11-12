using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class StoreTests
{
    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var store = new Store(id, "Fresh Mart", "1 High Street");

        Assert.Equal((id, "Fresh Mart", "1 High Street"), (store.Id, store.Name, store.Address));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Store(Guid.Empty, "Fresh Mart", "1 High Street"));

    [Fact]
    public void Constructor_RejectsAnIdThatIsNotVersion7()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Store(Guid.NewGuid(), "Fresh Mart", "1 High Street"));

        Assert.Contains("version 7", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankNameOrAddress(string blank)
    {
        Assert.Throws<ArgumentException>(() => new Store(Guid.CreateVersion7(), blank, "1 High Street"));
        Assert.Throws<ArgumentException>(() => new Store(Guid.CreateVersion7(), "Fresh Mart", blank));
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Store(Guid.CreateVersion7(), null!, "1 High Street"));
        Assert.Throws<ArgumentNullException>(() => new Store(Guid.CreateVersion7(), "Fresh Mart", null!));
    }

    [Fact]
    public void Constructor_StoresTheNameAndAddressTrimmed()
    {
        var store = new Store(Guid.CreateVersion7(), "  Fresh Mart  ", "\t1 High Street ");

        Assert.Equal(("Fresh Mart", "1 High Street"), (store.Name, store.Address));
    }

    [Fact]
    public void Constructor_AcceptsTheMaximumLengths_AndRejectsOneCharacterMore()
    {
        Assert.Equal(Store.MaxNameLength, new Store(Guid.CreateVersion7(), new string('a', Store.MaxNameLength), "x").Name.Length);
        Assert.Equal(Store.MaxAddressLength, new Store(Guid.CreateVersion7(), "x", new string('a', Store.MaxAddressLength)).Address.Length);
        Assert.Throws<ArgumentException>(() => new Store(Guid.CreateVersion7(), new string('a', Store.MaxNameLength + 1), "x"));
        Assert.Throws<ArgumentException>(() => new Store(Guid.CreateVersion7(), "x", new string('a', Store.MaxAddressLength + 1)));
    }

    [Fact]
    public void ThePaddingOfAName_DoesNotCountTowardsItsLength() =>
        Assert.Equal(Store.MaxNameLength, new Store(Guid.CreateVersion7(), $" {new string('a', Store.MaxNameLength)} ", "x").Name.Length);
}
