namespace GrocerEase.Domain.Entities;

/// <summary>
/// One line of the <b>stock ledger</b>: a change to the quantity of a product, by how much (<see cref="Delta"/>, negative to remove), why
/// (<see cref="Reason"/>), who did it, which order it was for, and when. The ledger is <b>append-only</b>: a row is never changed or
/// deleted, so the sum of a product's deltas is its quantity, and any difference is a bug that the ledger makes visible.
/// </summary>
/// <remarks>
/// A movement belongs to the store of its product and says so itself (<see cref="StoreId"/>), tied by the same composite foreign key as
/// the stock row. It is made by the same SQL statement that changes the quantity (see <c>IInventoryItemRepository</c>), never separately,
/// so the two cannot disagree. This entity is the shape of a row, and the rules a row must keep: the database repeats them as
/// constraints, so a row that makes no sense is refused whatever wrote it.
/// </remarks>
public sealed class StockMovement
{
    public const int RequiredIdVersion = 7;

    public StockMovement(
        Guid id, Guid productId, Guid storeId, int delta, StockMovementReason reason, Guid? actorId, Guid? orderId, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(productId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a known reason.");
        }

        // A change of nothing is not a movement; and no single change is bigger than a product may hold.
        ArgumentOutOfRangeException.ThrowIfZero(delta);
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, -InventoryItem.MaxQuantity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delta, InventoryItem.MaxQuantity);

        // The direction each reason allows: a delivery or a cancellation adds, an order removes, a correction may do either.
        var directionIsRight = reason switch
        {
            StockMovementReason.Correction => true,
            StockMovementReason.OrderPlaced => delta < 0,
            _ => delta > 0,
        };
        if (!directionIsRight)
        {
            throw new ArgumentException($"A {reason} movement cannot have a change of {delta}.", nameof(delta));
        }

        // Only an order's movements say which order; only the opening balance has no one behind it.
        var isForAnOrder = reason is StockMovementReason.OrderPlaced or StockMovementReason.OrderCancelled;
        if (isForAnOrder != (orderId is not null) || orderId == Guid.Empty)
        {
            throw new ArgumentException(isForAnOrder ? $"A {reason} movement is for an order." : $"A {reason} movement is not for an order.", nameof(orderId));
        }

        if ((reason == StockMovementReason.OpeningBalance) != (actorId is null) || actorId == Guid.Empty)
        {
            throw new ArgumentException(
                reason == StockMovementReason.OpeningBalance ? "The opening balance has no one behind it." : $"A {reason} movement says who made it.", nameof(actorId));
        }

        Id = id;
        ProductId = productId;
        StoreId = storeId;
        Delta = delta;
        Reason = reason;
        ActorId = actorId;
        OrderId = orderId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    public Guid StoreId { get; }

    /// <summary>By how much the quantity changed: negative when stock was removed. Never zero.</summary>
    public int Delta { get; }

    public StockMovementReason Reason { get; }

    /// <summary>The user who made the change (a store user, or the customer whose order it was); <c>null</c> only for the opening balance.</summary>
    public Guid? ActorId { get; }

    /// <summary>The order that took or returned the stock; <c>null</c> for everything else.</summary>
    public Guid? OrderId { get; }

    public DateTimeOffset CreatedAt { get; }
}
