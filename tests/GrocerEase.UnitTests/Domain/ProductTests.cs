using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class ProductTests
{
    private static readonly Guid StoreId = Guid.CreateVersion7();

    private static Product Milk(decimal price = 2.50m) => new(Guid.CreateVersion7(), StoreId, "Milk", "Fresh milk", price);

    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var product = new Product(id, StoreId, "Milk", "Fresh milk", 2.50m);

        Assert.Equal((id, StoreId, "Milk", "Fresh milk", 2.50m, null, false), (product.Id, product.StoreId, product.Name, product.Description, product.Price, product.ArchivedAt, product.IsArchived));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId_AnIdThatIsNotVersion7_AndAnEmptyStore()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product(Guid.Empty, StoreId, "Milk", "", 1m));
        Assert.Contains("version 7", Assert.Throws<ArgumentException>(() => new Product(Guid.NewGuid(), StoreId, "Milk", "", 1m)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product(Guid.CreateVersion7(), Guid.Empty, "Milk", "", 1m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankName(string name) =>
        Assert.Throws<ArgumentException>(() => new Product(Guid.CreateVersion7(), StoreId, name, "", 1m));

    [Fact]
    public void Constructor_RejectsNullText()
    {
        Assert.Throws<ArgumentNullException>(() => new Product(Guid.CreateVersion7(), StoreId, null!, "", 1m));
        Assert.Throws<ArgumentNullException>(() => new Product(Guid.CreateVersion7(), StoreId, "Milk", null!, 1m));
    }

    [Fact]
    public void TheDescription_MayBeEmpty()
    {
        Assert.Equal(string.Empty, new Product(Guid.CreateVersion7(), StoreId, "Milk", "   ", 1m).Description);
    }

    [Fact]
    public void Constructor_TrimsTheNameAndTheDescription()
    {
        var product = new Product(Guid.CreateVersion7(), StoreId, "  Milk ", " Fresh ", 1m);

        Assert.Equal(("Milk", "Fresh"), (product.Name, product.Description));
    }

    [Fact]
    public void Constructor_AcceptsTheMaximumLengths_AndRejectsOneCharacterMore()
    {
        Assert.Equal(Product.MaxNameLength, new Product(Guid.CreateVersion7(), StoreId, new string('a', Product.MaxNameLength), "", 1m).Name.Length);
        Assert.Equal(Product.MaxDescriptionLength, new Product(Guid.CreateVersion7(), StoreId, "Milk", new string('a', Product.MaxDescriptionLength), 1m).Description.Length);
        Assert.Throws<ArgumentException>(() => new Product(Guid.CreateVersion7(), StoreId, new string('a', Product.MaxNameLength + 1), "", 1m));
        Assert.Throws<ArgumentException>(() => new Product(Guid.CreateVersion7(), StoreId, "Milk", new string('a', Product.MaxDescriptionLength + 1), 1m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    public void APriceThatIsNotPositive_IsRefused(string price) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Milk(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void ThePriceMayBeTheSmallestAmountOfMoney_AndTheLargestTheColumnHolds()
    {
        Assert.Equal(0.01m, Milk(0.01m).Price);
        Assert.Equal(Product.MaxPrice, Milk(Product.MaxPrice).Price);
        Assert.Throws<ArgumentOutOfRangeException>(() => Milk(Product.MaxPrice + 0.01m));
    }

    [Theory]
    [InlineData("1.001")]
    [InlineData("0.005")]
    public void APriceWithHalfCents_IsRefused_NotRoundedQuietly(string price) =>
        Assert.Throws<ArgumentException>(() => Milk(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void ThePrice_KeepsTrailingZeros_AsMoneyDoes()
    {
        Assert.Equal(1.10m, Milk(1.10m).Price);
        Assert.Equal(1m, Milk(1.00m).Price);
    }

    [Fact]
    public void WithDetails_ReturnsAnEditedCopy_AndKeepsTheIdTheStoreAndTheArchivedState()
    {
        var archivedAt = DateTimeOffset.UtcNow;
        var original = new Product(Guid.CreateVersion7(), StoreId, "Milk", "Fresh", 2.50m, archivedAt);

        var edited = original.WithDetails("Oat milk", "Vegan", 3.20m);

        Assert.Equal(("Oat milk", "Vegan", 3.20m), (edited.Name, edited.Description, edited.Price));
        Assert.Equal((original.Id, original.StoreId, archivedAt), (edited.Id, edited.StoreId, edited.ArchivedAt));
        Assert.Equal("Milk", original.Name); // the original is untouched
    }

    [Fact]
    public void WithDetails_RefusesWhatTheConstructorRefuses() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Milk().WithDetails("Milk", "", 0m));

    [Fact]
    public void ANewProduct_IsVersionOne()
    {
        Assert.Equal(1, Milk().Version);
        Assert.Equal(Product.FirstVersion, Milk().Version);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void AVersionOfOneOrMore_IsKept(int version) =>
        Assert.Equal(version, new Product(Guid.CreateVersion7(), StoreId, "Milk", "", 1m, null, version).Version);

    [Theory]
    [InlineData(0)] // "never read": a client that sent nothing
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AVersionBelowOne_IsRefused(int version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product(Guid.CreateVersion7(), StoreId, "Milk", "", 1m, null, version));

    [Fact]
    public void AnEditedCopy_IsTheNextVersion_AndKeepsTheRest()
    {
        var archivedAt = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);
        var product = new Product(Guid.CreateVersion7(), StoreId, "Milk", "", 1m, archivedAt, 4);

        var edited = product.WithDetails("Oat milk", "Vegan", 3m);

        Assert.Equal((5, product.Id, StoreId, archivedAt), (edited.Version, edited.Id, edited.StoreId, edited.ArchivedAt));
        Assert.Equal(4, product.Version); // the original is not changed: the entity is immutable
    }
}
