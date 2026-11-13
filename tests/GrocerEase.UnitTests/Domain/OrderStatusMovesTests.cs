using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

/// <summary>The life of an order: Pending, then Verified, then Shipped, or Cancelled while it has not shipped. Shipped and Cancelled are final.</summary>
public class OrderStatusMovesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Actions = ["Verify", "Ship", "Cancel", "Withdraw"];

    private static Order InState(OrderStatus status)
    {
        var id = Guid.CreateVersion7();
        var line = new OrderLine(Guid.CreateVersion7(), id, Guid.CreateVersion7(), "Milk", 2.50m, 2);
        var order = Order.Place(id, Guid.CreateVersion7(), Guid.CreateVersion7(), "1 High Street", Now, [line]);

        // The only way to get there is the way an order really gets there.
        Assert.True(status switch
        {
            OrderStatus.Pending => true,
            OrderStatus.Verified => order.Verify(),
            OrderStatus.Shipped => order.Verify() && order.Ship(),
            OrderStatus.Cancelled => order.Cancel(),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        });
        Assert.Equal(status, order.Status);
        return order;
    }

    private static bool Do(Order order, string action) => action switch
    {
        "Verify" => order.Verify(),
        "Ship" => order.Ship(),
        "Cancel" => order.Cancel(),
        "Withdraw" => order.Withdraw(),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    // Every one of the 4 states by the 4 actions: the legal moves are listed, the other eleven are refused. Withdraw is the customer's own cancellation: legal only while Pending.
    private static readonly (OrderStatus From, string Action, bool Allowed, OrderStatus Expected)[] Moves =
    [
        (OrderStatus.Pending, "Verify", true, OrderStatus.Verified),
        (OrderStatus.Pending, "Ship", false, OrderStatus.Pending),
        (OrderStatus.Pending, "Cancel", true, OrderStatus.Cancelled),
        (OrderStatus.Pending, "Withdraw", true, OrderStatus.Cancelled),
        (OrderStatus.Verified, "Verify", false, OrderStatus.Verified),
        (OrderStatus.Verified, "Ship", true, OrderStatus.Shipped),
        (OrderStatus.Verified, "Cancel", true, OrderStatus.Cancelled),
        (OrderStatus.Verified, "Withdraw", false, OrderStatus.Verified),
        (OrderStatus.Shipped, "Verify", false, OrderStatus.Shipped),
        (OrderStatus.Shipped, "Ship", false, OrderStatus.Shipped),
        (OrderStatus.Shipped, "Cancel", false, OrderStatus.Shipped),
        (OrderStatus.Shipped, "Withdraw", false, OrderStatus.Shipped),
        (OrderStatus.Cancelled, "Verify", false, OrderStatus.Cancelled),
        (OrderStatus.Cancelled, "Ship", false, OrderStatus.Cancelled),
        (OrderStatus.Cancelled, "Cancel", false, OrderStatus.Cancelled),
        (OrderStatus.Cancelled, "Withdraw", false, OrderStatus.Cancelled),
    ];

    public static TheoryData<OrderStatus, string, bool, OrderStatus> MoveRows()
    {
        var data = new TheoryData<OrderStatus, string, bool, OrderStatus>();
        foreach (var (from, action, allowed, expected) in Moves)
        {
            data.Add(from, action, allowed, expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MoveRows))]
    public void EachAction_IsLegalFromOneOrTwoStatesOnly_AndARefusedMoveChangesNothing(OrderStatus from, string action, bool allowed, OrderStatus expected)
    {
        var order = InState(from);

        var moved = Do(order, action);

        Assert.Equal((allowed, expected), (moved, order.Status));
    }

    [Fact]
    public void TheTable_CoversEveryStatusAndEveryAction()
    {
        // If a status is added, this fails until the table above says what each action does from it.
        var expected = from status in Enum.GetValues<OrderStatus>() from action in Actions select (status, action);

        Assert.Equal(expected.Order(), Moves.Select(row => (row.From, row.Action)).Order());
        Assert.Equal(Actions.Order(StringComparer.Ordinal), Moves.Select(row => row.Action).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    [Fact]
    public void AMove_ChangesNothingButTheStatus()
    {
        var order = InState(OrderStatus.Pending);
        var (id, store, customer, address, placed, total, lines) = (order.Id, order.StoreId, order.CustomerId, order.DeliveryAddress, order.PlacedAt, order.Total, order.Lines.ToList());

        Assert.True(order.Verify());
        Assert.True(order.Ship());

        Assert.Equal((id, store, customer, address, placed, total), (order.Id, order.StoreId, order.CustomerId, order.DeliveryAddress, order.PlacedAt, order.Total));
        Assert.Equal(lines, order.Lines);
    }

    [Fact]
    public void ACancelledOrderAndAShippedOne_CanNeverMoveAgain()
    {
        foreach (var final in new[] { OrderStatus.Cancelled, OrderStatus.Shipped })
        {
            var order = InState(final);

            Assert.False(order.Verify() || order.Ship() || order.Cancel() || order.Withdraw());
            Assert.Equal(final, order.Status);
        }
    }
}
