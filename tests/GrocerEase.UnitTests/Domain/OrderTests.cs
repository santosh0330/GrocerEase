using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class OrderTests
{
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid CustomerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static OrderLine Line(Guid orderId, decimal price = 2.50m, int quantity = 2, Guid? productId = null) =>
        new(Guid.CreateVersion7(), orderId, productId ?? Guid.CreateVersion7(), "Milk", price, quantity);

    private static Order Place(Guid? id = null, string address = "1 High Street", params OrderLine[] lines)
    {
        var orderId = id ?? Guid.CreateVersion7();
        return Order.Place(orderId, StoreId, CustomerId, address, Now, lines.Length == 0 ? [Line(orderId)] : lines);
    }

    [Fact]
    public void Place_MakesAPendingOrder_WithTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();
        var line = Line(id);

        var order = Order.Place(id, StoreId, CustomerId, "1 High Street", Now, [line]);

        Assert.Equal((id, StoreId, CustomerId, OrderStatus.Pending, "1 High Street", Now), (order.Id, order.StoreId, order.CustomerId, order.Status, order.DeliveryAddress, order.PlacedAt));
        Assert.Equal([line], order.Lines);
    }

    [Fact]
    public void TheTotal_IsComputedFromTheLines()
    {
        var id = Guid.CreateVersion7();

        var order = Order.Place(id, StoreId, CustomerId, "x", Now, [Line(id, 2.50m, 2), Line(id, 1.10m, 3)]);

        Assert.Equal((2.50m * 2) + (1.10m * 3), order.Total);
        Assert.Equal(8.30m, order.Total);
    }

    [Fact]
    public void AnOrder_WithNoLines_CannotBeMade()
    {
        var exception = Assert.Throws<ArgumentException>(() => Order.Place(Guid.CreateVersion7(), StoreId, CustomerId, "x", Now, []));

        Assert.Contains("at least one line", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProductTwiceInOneOrder_IsRefused_ABiggerQuantityIsHowToAskForMore()
    {
        var id = Guid.CreateVersion7();
        var product = Guid.CreateVersion7();

        var exception = Assert.Throws<ArgumentException>(() => Order.Place(id, StoreId, CustomerId, "x", Now, [Line(id, productId: product), Line(id, productId: product)]));

        Assert.Contains("twice", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALineOfAnotherOrder_IsRefused()
    {
        var id = Guid.CreateVersion7();

        Assert.Throws<ArgumentException>(() => Order.Place(id, StoreId, CustomerId, "x", Now, [Line(Guid.CreateVersion7())]));
    }

    [Fact]
    public void AnOrder_MayHoldTheMaximumNumberOfLines_AndNotOneMore()
    {
        var id = Guid.CreateVersion7();
        var atTheMaximum = Enumerable.Range(0, Order.MaxLines).Select(_ => Line(id)).ToList();

        Assert.Equal(Order.MaxLines, Order.Place(id, StoreId, CustomerId, "x", Now, atTheMaximum).Lines.Count);
        Assert.Throws<ArgumentException>(() => Order.Place(id, StoreId, CustomerId, "x", Now, [.. atTheMaximum, Line(id)]));
    }

    [Fact]
    public void TheLines_CannotBeChangedThroughTheProperty()
    {
        var order = Place();

        Assert.False(order.Lines is List<OrderLine>); // a read-only wrapper, not the list itself
        Assert.Throws<NotSupportedException>(() => ((ICollection<OrderLine>)order.Lines).Add(Line(order.Id)));
    }

    [Fact]
    public void Place_RejectsAnEmptyId_AnIdThatIsNotVersion7_AndEmptyStoreOrCustomer()
    {
        var id = Guid.CreateVersion7();

        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Place(Guid.Empty, StoreId, CustomerId, "x", Now, [Line(Guid.Empty)]));
        Assert.Contains("version 7", Assert.Throws<ArgumentException>(() => Order.Place(Guid.NewGuid(), StoreId, CustomerId, "x", Now, [Line(Guid.NewGuid())])).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Place(id, Guid.Empty, CustomerId, "x", Now, [Line(id)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Order.Place(id, StoreId, Guid.Empty, "x", Now, [Line(id)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankDeliveryAddress_IsRefused(string address) =>
        Assert.Throws<ArgumentException>(() => Place(address: address));

    [Fact]
    public void TheDeliveryAddress_IsTrimmed_AndMeasuredAfterwards()
    {
        Assert.Equal("1 High Street", Place(address: "  1 High Street ").DeliveryAddress);
        Assert.Equal(Order.MaxDeliveryAddressLength, Place(address: $" {new string('a', Order.MaxDeliveryAddressLength)} ").DeliveryAddress.Length);
        Assert.Throws<ArgumentException>(() => Place(address: new string('a', Order.MaxDeliveryAddressLength + 1)));
    }

    [Fact]
    public void Place_RejectsNullLines() =>
        Assert.Throws<ArgumentNullException>(() => Order.Place(Guid.CreateVersion7(), StoreId, CustomerId, "x", Now, null!));
}
