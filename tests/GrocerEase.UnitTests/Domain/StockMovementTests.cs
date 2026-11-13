using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Domain;

public class StockMovementTests
{
    private static readonly Guid ProductId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly Guid Order = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private static StockMovement Make(int delta, StockMovementReason reason, Guid? actor = null, Guid? order = null, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), ProductId, StoreId, delta, reason, actor, order, Now);

    [Fact]
    public void Constructor_KeepsTheValuesItWasGiven()
    {
        var id = Guid.CreateVersion7();

        var movement = new StockMovement(id, ProductId, StoreId, -3, StockMovementReason.OrderPlaced, Actor, Order, Now);

        Assert.Equal((id, ProductId, StoreId, -3, StockMovementReason.OrderPlaced), (movement.Id, movement.ProductId, movement.StoreId, movement.Delta, movement.Reason));
        Assert.Equal((Actor, Order, Now), (movement.ActorId, movement.OrderId, movement.CreatedAt));
    }

    // One legal movement for every reason, each in the direction and with the order and actor that reason needs.
    [Fact]
    public void EachReason_HasALegalShape()
    {
        Make(5, StockMovementReason.OpeningBalance);
        Make(5, StockMovementReason.Received, Actor);
        Make(5, StockMovementReason.Correction, Actor);
        Make(-5, StockMovementReason.Correction, Actor);
        Make(-5, StockMovementReason.OrderPlaced, Actor, Order);
        Make(5, StockMovementReason.OrderCancelled, Actor, Order);
    }

    [Theory]
    [InlineData(StockMovementReason.Received)]
    [InlineData(StockMovementReason.Correction)]
    [InlineData(StockMovementReason.OrderPlaced)]
    [InlineData(StockMovementReason.OrderCancelled)]
    public void AChangeOfZero_IsNotAMovement(StockMovementReason reason) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(0, reason, Actor, reason is StockMovementReason.OrderPlaced or StockMovementReason.OrderCancelled ? Order : null));

    [Fact]
    public void AChangeBiggerThanAProductMayHold_IsRefused_InEitherDirection()
    {
        Make(InventoryItem.MaxQuantity, StockMovementReason.Received, Actor);
        Make(-InventoryItem.MaxQuantity, StockMovementReason.Correction, Actor);
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(InventoryItem.MaxQuantity + 1, StockMovementReason.Received, Actor));
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(-InventoryItem.MaxQuantity - 1, StockMovementReason.Correction, Actor));
    }

    [Theory]
    [InlineData(StockMovementReason.OpeningBalance, -1)]
    [InlineData(StockMovementReason.Received, -1)] // a delivery adds
    [InlineData(StockMovementReason.OrderPlaced, 1)] // an order removes
    [InlineData(StockMovementReason.OrderCancelled, -1)] // a cancellation gives back
    public void AReason_RefusesTheWrongDirection(StockMovementReason reason, int delta)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            Make(delta, reason, reason == StockMovementReason.OpeningBalance ? null : Actor, reason is StockMovementReason.OrderPlaced or StockMovementReason.OrderCancelled ? Order : null));

        Assert.Equal("delta", exception.ParamName);
    }

    [Theory]
    [InlineData(StockMovementReason.OrderPlaced, -1)]
    [InlineData(StockMovementReason.OrderCancelled, 1)]
    public void AnOrdersMovement_MustSayWhichOrder(StockMovementReason reason, int delta) =>
        Assert.Equal("orderId", Assert.Throws<ArgumentException>(() => Make(delta, reason, Actor, null)).ParamName);

    [Theory]
    [InlineData(StockMovementReason.Received, 1)]
    [InlineData(StockMovementReason.Correction, 1)]
    [InlineData(StockMovementReason.OpeningBalance, 1)]
    public void AMovementThatIsNotForAnOrder_MustNotNameOne(StockMovementReason reason, int delta) =>
        Assert.Equal("orderId", Assert.Throws<ArgumentException>(() => Make(delta, reason, reason == StockMovementReason.OpeningBalance ? null : Actor, Order)).ParamName);

    [Fact]
    public void TheOpeningBalance_HasNoActor_AndEveryOtherMovementHasOne()
    {
        Assert.Equal("actorId", Assert.Throws<ArgumentException>(() => Make(5, StockMovementReason.OpeningBalance, Actor)).ParamName);
        foreach (var (delta, reason, order) in new (int, StockMovementReason, Guid?)[]
        {
            (5, StockMovementReason.Received, null), (5, StockMovementReason.Correction, null),
            (-5, StockMovementReason.OrderPlaced, Order), (5, StockMovementReason.OrderCancelled, Order),
        })
        {
            Assert.Equal("actorId", Assert.Throws<ArgumentException>(() => Make(delta, reason, null, order)).ParamName);
        }
    }

    [Fact]
    public void AnEmptyActorOrOrder_IsRefused_NotTreatedAsNone()
    {
        Assert.Throws<ArgumentException>(() => Make(5, StockMovementReason.Received, Guid.Empty));
        Assert.Throws<ArgumentException>(() => Make(-5, StockMovementReason.OrderPlaced, Actor, Guid.Empty));
    }

    [Fact]
    public void TheIds_MustBeSet_AndTheMovementIdAVersion7Uuid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(5, StockMovementReason.Received, Actor, null, Guid.Empty));
        Assert.Throws<ArgumentException>(() => Make(5, StockMovementReason.Received, Actor, null, Guid.NewGuid())); // a version 4
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockMovement(Guid.CreateVersion7(), Guid.Empty, StoreId, 5, StockMovementReason.Received, Actor, null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockMovement(Guid.CreateVersion7(), ProductId, Guid.Empty, 5, StockMovementReason.Received, Actor, null, Now));
    }

    [Fact]
    public void AnUnknownReason_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Make(5, (StockMovementReason)99, Actor));
}
