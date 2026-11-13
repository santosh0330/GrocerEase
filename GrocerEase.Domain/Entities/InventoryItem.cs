namespace GrocerEase.Domain.Entities;

/// <summary>
/// How many units of a product a store has on hand: one row per product, and <b>no row means none</b> (the first adjustment makes the
/// row). It belongs to the store of its product, and says so itself (<see cref="StoreId"/>): the database ties the two with a composite
/// foreign key, so an item cannot claim a store its product is not in.
/// </summary>
/// <remarks>
/// The quantity is changed by <b>adding or removing an amount</b> in one SQL statement, never by reading it and writing a new number,
/// so two changes at the same moment both count (see <c>IInventoryItemRepository</c>). This entity is the shape of the row and its
/// limits; the arithmetic is the database's.
/// </remarks>
public sealed class InventoryItem
{
    /// <summary>
    /// The most one product may hold. It is also what keeps the sum of a quantity and a change inside an <c>int</c> (twice this is below
    /// <see cref="int.MaxValue"/>), so the database never overflows in the middle of a statement.
    /// </summary>
    public const int MaxQuantity = 1_000_000_000;

    public InventoryItem(Guid productId, Guid storeId, int quantityOnHand)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(productId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(quantityOnHand); // never below zero: a store cannot have minus three bottles of milk
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantityOnHand, MaxQuantity);

        ProductId = productId;
        StoreId = storeId;
        QuantityOnHand = quantityOnHand;
    }

    public Guid ProductId { get; }

    public Guid StoreId { get; }

    public int QuantityOnHand { get; }
}
