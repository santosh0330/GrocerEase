namespace GrocerEase.Domain.Entities;

/// <summary>
/// One product bought in an <see cref="Order"/>, <b>as it was when it was bought</b>: the name and the unit price are copied from the
/// product at that moment, so a later price change, rename or archive does not rewrite what the customer ordered.
/// </summary>
public sealed class OrderLine
{
    public const int RequiredIdVersion = 7;

    public OrderLine(Guid id, Guid orderId, Guid productId, string productName, decimal unitPrice, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(orderId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(productId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        if (productName.Length > Product.MaxNameLength)
        {
            throw new ArgumentException($"The value must be at most {Product.MaxNameLength} characters.", nameof(productName));
        }

        // The same rules as a product's price: a real amount of money, with no half cents.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitPrice);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unitPrice, Product.MaxPrice);
        if (decimal.Round(unitPrice, Product.PriceScale) != unitPrice)
        {
            throw new ArgumentException($"The price must have at most {Product.PriceScale} decimal places.", nameof(unitPrice));
        }

        // At least one, and no more than a store could ever hold.
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, InventoryItem.MaxQuantity);

        Id = id;
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public Guid OrderId { get; }

    public Guid ProductId { get; }

    /// <summary>The product's name when it was bought.</summary>
    public string ProductName { get; }

    /// <summary>The product's price when it was bought.</summary>
    public decimal UnitPrice { get; }

    public int Quantity { get; }

    public decimal LineTotal => UnitPrice * Quantity;
}
