using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class OrderLineTests
{
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();

    private static OrderLine Make(decimal price = 2.50m, int quantity = 3, string name = "Milk") =>
        new(Guid.CreateVersion7(), OrderId, ProductId, name, price, quantity);

    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven_AndComputesTheLineTotal()
    {
        var line = Make(2.50m, 3);

        Assert.Equal((OrderId, ProductId, "Milk", 2.50m, 3, 7.50m), (line.OrderId, line.ProductId, line.ProductName, line.UnitPrice, line.Quantity, line.LineTotal));
    }

    [Fact]
    public void Constructor_RejectsAnEmptyId_AnIdThatIsNotVersion7_AndEmptyOrderOrProduct()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderLine(Guid.Empty, OrderId, ProductId, "Milk", 1m, 1));
        Assert.Contains("version 7", Assert.Throws<ArgumentException>(() => new OrderLine(Guid.NewGuid(), OrderId, ProductId, "Milk", 1m, 1)).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderLine(Guid.CreateVersion7(), Guid.Empty, ProductId, "Milk", 1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderLine(Guid.CreateVersion7(), OrderId, Guid.Empty, "Milk", 1m, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankProductName_IsRefused(string name) =>
        Assert.Throws<ArgumentException>(() => Make(name: name));

    [Fact]
    public void TheProductName_MayBeAsLongAsAProductsName_AndNotLonger()
    {
        Assert.Equal(Product.MaxNameLength, Make(name: new string('a', Product.MaxNameLength)).ProductName.Length);
        Assert.Throws<ArgumentException>(() => Make(name: new string('a', Product.MaxNameLength + 1)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void APriceThatIsNotPositive_IsRefused(string price) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void APriceWithHalfCents_OrTooBig_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => Make(1.005m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(Product.MaxPrice + 0.01m));
        Assert.Equal(Product.MaxPrice, Make(Product.MaxPrice).UnitPrice);
    }

    [Fact]
    public void TheQuantity_IsAtLeastOne_AndAtMostWhatAStoreCanHold()
    {
        Assert.Equal(1, Make(quantity: 1).Quantity);
        Assert.Equal(InventoryItem.MaxQuantity, Make(quantity: InventoryItem.MaxQuantity).Quantity);
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(quantity: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(quantity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(quantity: InventoryItem.MaxQuantity + 1));
    }
}
