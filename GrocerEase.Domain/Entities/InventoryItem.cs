using System.Linq.Expressions;

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

    /// <summary>The highest low-stock level: the same bound as the quantity, since a level above the most a product may hold would always be reached.</summary>
    public const int MaxLowStockThreshold = MaxQuantity;

    public InventoryItem(Guid productId, Guid storeId, int quantityOnHand, int? lowStockThreshold = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(productId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(quantityOnHand); // never below zero: a store cannot have minus three bottles of milk
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantityOnHand, MaxQuantity);
        if (lowStockThreshold is < 0 or > MaxLowStockThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(lowStockThreshold), lowStockThreshold, $"The level must be from 0 to {MaxLowStockThreshold}, or null for none.");
        }

        ProductId = productId;
        StoreId = storeId;
        QuantityOnHand = quantityOnHand;
        LowStockThreshold = lowStockThreshold;
    }

    public Guid ProductId { get; }

    public Guid StoreId { get; }

    public int QuantityOnHand { get; }

    /// <summary>
    /// The level at or below which the store wants to be told the product is running out, or <c>null</c> when it has set none (a product with
    /// no level is never "low"). Set by its own statement, like the quantity.
    /// </summary>
    public int? LowStockThreshold { get; }

    /// <summary>
    /// The one definition of "running out": a level is set, and the quantity is <b>at or below</b> it (a level of 5 is low at 5, and not at 6).
    /// It is an expression, not a method, so it is written once and used twice: compiled, it answers for an entity in memory
    /// (<see cref="IsLow"/>); handed to a query (<c>Where(InventoryItem.IsLowExpression)</c>), Entity Framework translates it to SQL. Nothing
    /// else says what "low" means, so the two cannot drift apart.
    /// </summary>
    public static readonly Expression<Func<InventoryItem, bool>> IsLowExpression =
        item => item.LowStockThreshold != null && item.QuantityOnHand <= item.LowStockThreshold;

    // Compiled once. Declared after the expression: static fields are initialised in the order they are written.
    private static readonly Func<InventoryItem, bool> CompiledIsLow = IsLowExpression.Compile();

    /// <summary>Whether the product is running out, by <see cref="IsLowExpression"/>.</summary>
    public bool IsLow => CompiledIsLow(this);
}
